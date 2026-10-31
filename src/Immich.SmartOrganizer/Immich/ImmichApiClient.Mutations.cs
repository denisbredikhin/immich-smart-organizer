using System.Text.Json;

namespace Immich.SmartOrganizer.Immich;

internal sealed partial class ImmichApiClient
{
    internal static readonly HttpRequestOptionsKey<bool> MutationRequest = new("ImmichMutation");

    private static bool IsReadOnlyRequest(HttpMethod method, string endpoint) =>
        method == HttpMethod.Get || method == HttpMethod.Post && endpoint.StartsWith("search/", StringComparison.Ordinal);

    public async Task<string> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var user = await SendAsync<JsonElement>(HttpMethod.Get, "users/me", null, true, cancellationToken);
        return RequiredString(user, "id", "users/me");
    }

    public async Task<AssetState> GetAssetStateAsync(string assetId, CancellationToken cancellationToken)
    {
        var endpoint = $"assets/{Uri.EscapeDataString(assetId)}";
        var asset = await SendAsync<JsonElement>(HttpMethod.Get, endpoint, null, true, cancellationToken);
        var id = RequiredString(asset, "id", endpoint);
        var ownerId = RequiredString(asset, "ownerId", endpoint);
        var marker = RequiredString(asset, "updatedAt", endpoint);
        var visibility = RequiredString(asset, "visibility", endpoint);
        if (id != assetId || !DateTimeOffset.TryParse(marker, out _)
            || visibility is not ("timeline" or "archive" or "hidden" or "locked"))
        {
            throw InvalidResponse(HttpMethod.Get, endpoint);
        }

        return new AssetState(ownerId, marker, visibility);
    }

    public async Task<string> FindOrCreateAlbumAsync(string name, string userId, CancellationToken cancellationToken)
    {
        var endpoint = $"albums?isOwned=true&name={Uri.EscapeDataString(name)}";
        var response = await SendAsync<JsonElement>(HttpMethod.Get, endpoint, null, true, cancellationToken);
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse(HttpMethod.Get, endpoint);
        }

        List<string> matches = [];
        foreach (var album in response.EnumerateArray())
        {
            var albumName = RequiredString(album, "albumName", endpoint);
            var id = ValidateAlbum(album, userId, endpoint);
            if (string.Equals(name, albumName, StringComparison.Ordinal))
            {
                matches.Add(id);
            }
        }

        if (matches.Count > 1)
        {
            throw new ImmichApiException(ImmichApiFailureKind.IncompatibleApi,
                "Multiple owned albums have the configured name; choose a unique album name before continuing.");
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        var created = await SendAsync<JsonElement>(HttpMethod.Post, "albums", new { albumName = name }, true, cancellationToken);
        if (RequiredString(created, "albumName", "albums") != name)
        {
            throw InvalidResponse(HttpMethod.Post, "albums");
        }

        return ValidateAlbum(created, userId, "albums");
    }

    public async Task<string> FindOrCreateTagAsync(string path, CancellationToken cancellationToken)
    {
        var segments = path.Split('/');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ImmichApiException(ImmichApiFailureKind.IncompatibleApi, "Tag paths must have non-empty segments.");
        }

        var tags = await SendAsync<JsonElement>(HttpMethod.Get, "tags", null, true, cancellationToken);
        if (tags.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse(HttpMethod.Get, "tags");
        }

        string? parentId = null;
        foreach (var name in segments)
        {
            List<string> matches = [];
            foreach (var tag in tags.EnumerateArray())
            {
                var tagName = RequiredString(tag, "name", "tags");
                var id = RequiredString(tag, "id", "tags");
                var parent = OptionalString(tag, "parentId");
                if (tagName == name && parent == parentId)
                {
                    matches.Add(id);
                }
            }

            if (matches.Count > 1)
            {
                throw new ImmichApiException(ImmichApiFailureKind.IncompatibleApi, "Multiple tags match the configured tag path.");
            }

            if (matches.Count == 1)
            {
                parentId = matches[0];
                continue;
            }

            var created = await SendAsync<JsonElement>(HttpMethod.Post, "tags", new { name, parentId }, true, cancellationToken);
            if (RequiredString(created, "name", "tags") != name || OptionalString(created, "parentId") != parentId)
            {
                throw InvalidResponse(HttpMethod.Post, "tags");
            }

            parentId = RequiredString(created, "id", "tags");
        }

        return parentId!;
    }

    public Task<bool> AddToAlbumAsync(string albumId, string assetId, CancellationToken cancellationToken) =>
        ApplyMembershipAsync($"albums/{Uri.EscapeDataString(albumId)}/assets", assetId, cancellationToken);

    public Task<bool> ApplyTagAsync(string tagId, string assetId, CancellationToken cancellationToken) =>
        ApplyMembershipAsync($"tags/{Uri.EscapeDataString(tagId)}/assets", assetId, cancellationToken);

    public async Task ArchiveAsync(string assetId, CancellationToken cancellationToken)
    {
        await SendAsync<NoContentResponse>(HttpMethod.Put, "assets",
            new { ids = new[] { assetId }, visibility = "archive" }, true, cancellationToken);
    }

    private async Task<bool> ApplyMembershipAsync(string endpoint, string assetId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<JsonElement>(HttpMethod.Put, endpoint,
            new { ids = new[] { assetId } }, true, cancellationToken);
        if (response.ValueKind != JsonValueKind.Array || response.GetArrayLength() != 1)
        {
            throw InvalidResponse(HttpMethod.Put, endpoint);
        }

        var result = response[0];
        if (RequiredString(result, "id", endpoint) != assetId
            || !result.TryGetProperty("success", out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidResponse(HttpMethod.Put, endpoint);
        }

        if (success.GetBoolean())
        {
            return true;
        }

        var reason = RequiredString(result, "error", endpoint);
        if (reason == "duplicate")
        {
            return false;
        }

        throw new ImmichApiException(
            reason == "no_permission" ? ImmichApiFailureKind.Authentication : ImmichApiFailureKind.IncompatibleApi,
            $"Immich did not confirm the membership action ({reason}).");
    }

    private static string ValidateAlbum(JsonElement album, string userId, string endpoint)
    {
        var id = RequiredString(album, "id", endpoint);
        if (!album.TryGetProperty("albumUsers", out var users)
            || users.ValueKind != JsonValueKind.Array || users.GetArrayLength() == 0
            || RequiredString(users[0], "role", endpoint) != "owner"
            || !users[0].TryGetProperty("user", out var owner)
            || RequiredString(owner, "id", endpoint) != userId)
        {
            throw new ImmichApiException(ImmichApiFailureKind.IncompatibleApi, "The album owner could not be verified; no membership action was sent.");
        }

        return id;
    }

    private static string RequiredString(JsonElement element, string name, string endpoint)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw InvalidResponse(HttpMethod.Get, endpoint);
        }

        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw InvalidResponse(HttpMethod.Get, "tags");
        }

        return property.GetString();
    }

    internal sealed record AssetState(string OwnerId, string UpdatedAt, string Visibility);

    private sealed record NoContentResponse;
}
