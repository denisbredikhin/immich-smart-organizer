using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Immich.SmartOrganizer.Immich;

internal sealed partial class ImmichApiClient
{
    private const int MinimumPageSize = 1;
    private const int MaximumPageSize = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _apiBaseAddress;
    private readonly string _apiKey;
    private readonly bool _includeArchived;
    private readonly bool _includeExternalLibraries;

    public ImmichApiClient(
        HttpClient httpClient, string serverUrl, string apiKey,
        bool includeArchived = true, bool includeExternalLibraries = true)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(serverUri.UserInfo)
            || !string.IsNullOrEmpty(serverUri.Query)
            || !string.IsNullOrEmpty(serverUri.Fragment))
        {
            throw new ArgumentException("The Immich URL must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.", nameof(serverUrl));
        }

        _httpClient = httpClient;
        _apiKey = apiKey;
        _includeArchived = includeArchived;
        _includeExternalLibraries = includeExternalLibraries;
        _apiBaseAddress = CreateApiBaseAddress(serverUri);
    }

    public async Task<ImmichServerVersion> GetServerVersionAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<ServerVersionResponse>(
            HttpMethod.Get,
            "server/version",
            requestBody: null,
            authenticated: false,
            cancellationToken);

        if (response.Major is null
            || response.Minor is null
            || response.Patch is null
            || response.Prerelease is null)
        {
            throw InvalidResponse(HttpMethod.Get, "server/version");
        }

        ImmichServerVersion version = new(
            response.Major.Value,
            response.Minor.Value,
            response.Patch.Value,
            response.Prerelease.Value);
        if (!version.SupportsSearchApiV2)
        {
            throw new ImmichApiException(
                ImmichApiFailureKind.IncompatibleApi,
                $"Immich server version {version} is not supported. Stable Immich 3.2.0 or later in the 3.x series is required.");
        }

        return version;
    }

    public async Task<ImmichSearchPage> SearchMetadataAsync(
        string query,
        int size,
        string? cursor = null,
        bool includeVideos = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ValidatePageSize(size);

        Dictionary<string, object> filter = new(StringComparer.Ordinal)
        {
            ["ocr"] = new { matches = query }
        };
        if (!includeVideos)
        {
            filter["type"] = new { eq = "IMAGE" };
        }
        AddScopeFilters(filter);

        Dictionary<string, object> requestBody = new(StringComparer.Ordinal)
        {
            ["size"] = size,
            ["filter"] = filter
        };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            requestBody["cursor"] = cursor;
        }

        var response = await SendAsync<SearchResponse>(
            HttpMethod.Post,
            "search/metadata",
            requestBody,
            authenticated: true,
            cancellationToken);
        return MapSearchResponse(response, HttpMethod.Post, "search/metadata");
    }

    public async Task<ImmichSearchPage> SearchSmartAsync(
        string query,
        int size,
        bool includeVideos = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ValidatePageSize(size);

        Dictionary<string, object> filter = new(StringComparer.Ordinal);
        if (!includeVideos)
        {
            filter["type"] = new { eq = "IMAGE" };
        }
        AddScopeFilters(filter);

        var requestBody = new
        {
            query,
            size,
            filter
        };

        var response = await SendAsync<SearchResponse>(
            HttpMethod.Post,
            "search/smart",
            requestBody,
            authenticated: true,
            cancellationToken);
        return MapSearchResponse(response, HttpMethod.Post, "search/smart");
    }

    private void AddScopeFilters(Dictionary<string, object> filter)
    {
        if (!_includeArchived)
        {
            filter["visibility"] = new { eq = "timeline" };
        }

        if (!_includeExternalLibraries)
        {
            filter["libraryId"] = new Dictionary<string, object?> { ["eq"] = null };
        }
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string endpoint,
        object? requestBody,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(_apiBaseAddress, endpoint);
        using HttpRequestMessage request = new(method, requestUri);
        request.Options.Set(MutationRequest, !IsReadOnlyRequest(method, endpoint));
        if (authenticated)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        }

        if (requestBody is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ImmichApiException(
                ImmichApiFailureKind.Connectivity,
                $"Could not connect to Immich while requesting {endpoint}.",
                exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new ImmichApiException(
                ImmichApiFailureKind.Connectivity,
                $"The Immich request to {endpoint} timed out.",
                exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new ImmichApiException(
                ImmichApiFailureKind.Transient,
                $"Immich is temporarily unavailable while requesting {endpoint}.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateStatusException(endpoint, response.StatusCode);
            }

            if (typeof(TResponse) == typeof(NoContentResponse))
            {
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    throw InvalidResponse(method, endpoint);
                }

                return (TResponse)(object)new NoContentResponse();
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var result = await JsonSerializer.DeserializeAsync<TResponse>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                return result ?? throw InvalidResponse(method, endpoint);
            }
            catch (JsonException exception)
            {
                throw new ImmichApiException(
                    ImmichApiFailureKind.IncompatibleApi,
                    $"Immich returned an invalid response for {method} {endpoint}.",
                    exception);
            }
        }
    }

    private static ImmichSearchPage MapSearchResponse(
        SearchResponse response,
        HttpMethod method,
        string endpoint)
    {
        if (response.Assets?.Items is null || response.Assets.Count is null)
        {
            throw InvalidResponse(method, endpoint);
        }

        List<string> assetIds = new(response.Assets.Items.Count);
        foreach (var item in response.Assets.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id))
            {
                throw InvalidResponse(method, endpoint);
            }

            assetIds.Add(item.Id);
        }

        return new ImmichSearchPage(
            assetIds,
            response.Assets.Count.Value,
            response.Assets.NextCursor,
            response.Assets.NextPage);
    }

    private static ImmichApiException CreateStatusException(string endpoint, HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new ImmichApiException(
                ImmichApiFailureKind.Authentication,
                $"Immich rejected the request to {endpoint} with HTTP {(int)statusCode} (authentication or permission failure).");
        }

        if (IsRetryable(statusCode))
        {
            return new ImmichApiException(
                ImmichApiFailureKind.Transient,
                $"Immich returned HTTP {(int)statusCode} for {endpoint} after the retry limit was reached.");
        }

        if (statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.NotFound
            or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.UnsupportedMediaType
            or HttpStatusCode.UnprocessableEntity
            || (int)statusCode is >= 300 and < 400)
        {
            return new ImmichApiException(
                ImmichApiFailureKind.IncompatibleApi,
                $"Immich rejected the request to {endpoint} with HTTP {(int)statusCode}; the API endpoint or capability may be unavailable.");
        }

        return new ImmichApiException(
            ImmichApiFailureKind.IncompatibleApi,
            $"Immich returned unexpected HTTP {(int)statusCode} for {endpoint}.");
    }

    private static ImmichApiException InvalidResponse(HttpMethod method, string endpoint) =>
        new(
            ImmichApiFailureKind.IncompatibleApi,
            $"Immich returned an invalid response for {method} {endpoint}.");

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == (HttpStatusCode)429
        || (int)statusCode >= 500;

    private static void ValidatePageSize(int size)
    {
        if (size is < MinimumPageSize or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"Page size must be between {MinimumPageSize} and {MaximumPageSize}.");
        }
    }

    private static Uri CreateApiBaseAddress(Uri serverUri)
    {
        var path = serverUri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            path += "/api";
        }

        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        return new UriBuilder(serverUri) { Path = path }.Uri;
    }

    private sealed class ServerVersionResponse
    {
        [JsonPropertyName("major")]
        public int? Major { get; init; }

        [JsonPropertyName("minor")]
        public int? Minor { get; init; }

        [JsonPropertyName("patch")]
        public int? Patch { get; init; }

        [JsonPropertyName("prerelease")]
        public int? Prerelease { get; init; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("assets")]
        public SearchAssetPage? Assets { get; init; }
    }

    private sealed class SearchAssetPage
    {
        [JsonPropertyName("count")]
        public int? Count { get; init; }

        [JsonPropertyName("items")]
        public List<SearchAsset>? Items { get; init; }

        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; init; }

        [JsonPropertyName("nextPage")]
        public string? NextPage { get; init; }
    }

    private sealed class SearchAsset
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
