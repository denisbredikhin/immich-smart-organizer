using System.Net;
using System.Text.Json;
using Immich.SmartOrganizer.Immich;
using Microsoft.Extensions.DependencyInjection;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ImmichApiClientTests
{
    private const string ApiKey = "private-api-key";

    [TestMethod]
    public async Task GetServerVersion_does_not_send_api_key_and_uses_immich_api_path()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var version = await client.GetServerVersionAsync(TestContext.CancellationToken);

        Assert.AreEqual("3.3.5", version.ToString());
        Assert.AreEqual("/api/server/version", handler.Requests.Single().Uri.AbsolutePath);
        Assert.IsFalse(handler.Requests.Single().Headers.ContainsKey("x-api-key"));
    }

    [TestMethod]
    public async Task GetServerVersion_rejects_versions_without_stable_search_api_v2()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"major":3,"minor":1,"patch":9,"prerelease":0}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.GetServerVersionAsync(TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
        Assert.Contains("3.1.9", exception.Message);
    }

    [TestMethod]
    public async Task SearchMetadata_sends_v2_ocr_filter_and_cursor()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"assets":{"count":0,"items":[],"nextCursor":null,"nextPage":null}}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        await client.SearchMetadataAsync("fácture", 23, "cursor-value", cancellationToken: TestContext.CancellationToken);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("/api/search/metadata", request.Uri.AbsolutePath);
        Assert.AreEqual(ApiKey, request.Headers["x-api-key"].Single());
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.AreEqual(23, body.RootElement.GetProperty("size").GetInt32());
        Assert.AreEqual("cursor-value", body.RootElement.GetProperty("cursor").GetString());
        Assert.AreEqual(
            "fácture",
            body.RootElement.GetProperty("filter").GetProperty("ocr").GetProperty("matches").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("ocr", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("page", out _));
    }

    [TestMethod]
    public async Task SearchSmart_sends_query_with_empty_filter_and_no_inferred_score()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"assets":{"count":1,"items":[{"id":"asset-1"}],"nextCursor":null,"nextPage":null}}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var page = await client.SearchSmartAsync("printed invoice", 17, cancellationToken: TestContext.CancellationToken);

        var request = handler.Requests.Single();
        Assert.AreEqual("/api/search/smart", request.Uri.AbsolutePath);
        Assert.AreEqual(ApiKey, request.Headers["x-api-key"].Single());
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.AreEqual("printed invoice", body.RootElement.GetProperty("query").GetString());
        Assert.AreEqual(17, body.RootElement.GetProperty("size").GetInt32());
        Assert.AreEqual(JsonValueKind.Object, body.RootElement.GetProperty("filter").ValueKind);
        Assert.IsEmpty(body.RootElement.GetProperty("filter").EnumerateObject());
        Assert.AreEqual("asset-1", page.AssetIds.Single());
        Assert.IsFalse(body.RootElement.TryGetProperty("cursor", out _));
    }

    [TestMethod]
    public async Task SearchMetadata_retries_rate_limit_and_server_error_with_a_bounded_attempt_count()
    {
        var attempt = 0;
        StubHttpMessageHandler handler = new((_, _) =>
        {
            attempt++;
            var statusCode = attempt switch
            {
                1 => (HttpStatusCode)429,
                2 => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.OK
            };
            return Task.FromResult(StubHttpMessageHandler.Json(
                """{"assets":{"count":0,"items":[],"nextCursor":null,"nextPage":null}}""",
                statusCode));
        });
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        await client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(3, handler.Requests);
    }

    [TestMethod]
    public async Task SearchMetadata_retries_network_failures()
    {
        var attempt = 0;
        StubHttpMessageHandler handler = new((_, _) =>
        {
            attempt++;
            return attempt == 1
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("temporary network failure"))
                : Task.FromResult(StubHttpMessageHandler.Json(
                    """{"assets":{"count":0,"items":[],"nextCursor":null,"nextPage":null}}"""));
        });
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        await client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task SearchMetadata_maps_exhausted_network_failures()
    {
        StubHttpMessageHandler handler = new((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("network unavailable")));
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.Connectivity, exception.Kind);
        Assert.HasCount(3, handler.Requests);
    }

    [TestMethod]
    public async Task SearchMetadata_retries_when_response_body_read_fails()
    {
        var attempt = 0;
        StubHttpMessageHandler handler = new((_, _) =>
        {
            attempt++;
            HttpResponseMessage response = attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingHttpContent() }
                : StubHttpMessageHandler.Json(
                    """{"assets":{"count":0,"items":[],"nextCursor":null,"nextPage":null}}""");
            return Task.FromResult(response);
        });
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var page = await client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken);

        Assert.IsEmpty(page.AssetIds);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task SearchMetadata_does_not_retry_authentication_errors_or_expose_api_key()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("the server must not echo credentials", HttpStatusCode.Forbidden)));
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.Authentication, exception.Kind);
        Assert.HasCount(1, handler.Requests);
        Assert.IsFalse(exception.ToString().Contains(ApiKey, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SearchMetadata_maps_exhausted_transient_failures()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("unavailable", HttpStatusCode.ServiceUnavailable)));
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.Transient, exception.Kind);
        Assert.HasCount(3, handler.Requests);
    }

    [TestMethod]
    public async Task GetServerVersion_maps_invalid_response_shapes()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.GetServerVersionAsync(TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
    }

    [TestMethod]
    public async Task SearchMetadata_maps_malformed_search_response()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"assets":{"count":1,"items":[{}]}}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
    }

    [TestMethod]
    public async Task SearchMetadata_maps_null_search_result_items_to_incompatible_api()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"assets":{"count":1,"items":[null]}}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
    }

    [TestMethod]
    public async Task SearchSmart_propagates_caller_cancellation()
    {
        TaskCompletionSource requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubHttpMessageHandler handler = new(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The canceled request should not produce a response.");
        });
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);
        using CancellationTokenSource cancellation = new();
        var request = client.SearchSmartAsync("receipt", 10, cancellationToken: cancellation.Token);
        await requestStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => request);
    }

    [TestMethod]
    public async Task Constructor_preserves_reverse_proxy_prefix_and_does_not_duplicate_api_path()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = new(httpClient, "https://photos.example/immich/api", ApiKey);

        await client.GetServerVersionAsync(TestContext.CancellationToken);

        Assert.AreEqual("/immich/api/server/version", handler.Requests.Single().Uri.AbsolutePath);
    }

    private static ImmichApiClient CreateClient(HttpClient httpClient) =>
        new(httpClient, "https://photos.example", ApiKey);

    private static ServiceProvider CreateResilienceServiceProvider(HttpMessageHandler handler)
    {
        ServiceCollection services = new();
        services.AddImmichHttpClient(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class FailingHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            Task.FromException(new HttpRequestException("simulated response body failure"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
