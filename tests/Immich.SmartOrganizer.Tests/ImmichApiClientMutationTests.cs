using System.Net;
using System.Text.Json;
using Immich.SmartOrganizer.Immich;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ImmichApiClientMutationTests
{
    private const string UserId = "user-1";

    [TestMethod]
    public async Task Finds_owned_album_without_creating_a_duplicate()
    {
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? StubHttpMessageHandler.Json("""[{"id":"album-7","albumName":"Receipts","albumUsers":[{"role":"owner","user":{"id":"user-1"}}]}]""")
                : throw new InvalidOperationException("An existing owned album must not be recreated.")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var albumId = await client.FindOrCreateAlbumAsync("Receipts", UserId, TestContext.CancellationToken);

        Assert.AreEqual("album-7", albumId);
        Assert.AreEqual(HttpMethod.Get, handler.Requests.Single().Method);
        Assert.AreEqual("/api/albums", handler.Requests.Single().Uri.AbsolutePath);
        Assert.AreEqual("?isOwned=true&name=Receipts", handler.Requests.Single().Uri.Query);
    }

    [TestMethod]
    public async Task Creates_album_when_no_owned_name_matches_and_validates_the_owner()
    {
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? StubHttpMessageHandler.Json("[]")
                : StubHttpMessageHandler.Json("""{"id":"album-new","albumName":"Family & Friends","albumUsers":[{"role":"owner","user":{"id":"user-1"}}]}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var albumId = await client.FindOrCreateAlbumAsync("Family & Friends", UserId, TestContext.CancellationToken);

        Assert.AreEqual("album-new", albumId);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual("Family%20%26%20Friends", handler.Requests[0].Uri.Query.Split("name=").Last());
        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        using JsonDocument body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.AreEqual("Family & Friends", body.RootElement.GetProperty("albumName").GetString());
    }

    [TestMethod]
    public async Task Rejects_duplicate_owned_album_names_before_any_membership_write()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubHttpMessageHandler.Json(
            """[{"id":"album-1","albumName":"Receipts","albumUsers":[{"role":"owner","user":{"id":"user-1"}}]},{"id":"album-2","albumName":"Receipts","albumUsers":[{"role":"owner","user":{"id":"user-1"}}]}]""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.FindOrCreateAlbumAsync("Receipts", UserId, TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
        Assert.Contains("Multiple owned albums", exception.Message);
        Assert.HasCount(1, handler.Requests);
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Put));
    }

    [TestMethod]
    public async Task Rejects_album_with_unverified_owner_without_creating_or_assigning_it()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubHttpMessageHandler.Json(
            """[{"id":"album-foreign","albumName":"Receipts","albumUsers":[{"role":"owner","user":{"id":"someone-else"}}]}]""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.FindOrCreateAlbumAsync("Receipts", UserId, TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
        Assert.Contains("owner could not be verified", exception.Message);
        Assert.HasCount(1, handler.Requests);
        Assert.IsFalse(handler.Requests.Any(static request => request.Method is var method && method != HttpMethod.Get));
    }

    [TestMethod]
    public async Task Creates_tag_path_segments_with_each_parent_id()
    {
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? StubHttpMessageHandler.Json("""[{"id":"tag-root","name":"Documents","parentId":null}]""")
                : request.Body!.Contains("\"name\":\"Receipts\"", StringComparison.Ordinal)
                    ? StubHttpMessageHandler.Json("""{"id":"tag-receipts","name":"Receipts","parentId":"tag-root"}""")
                    : StubHttpMessageHandler.Json("""{"id":"tag-2026","name":"2026","parentId":"tag-receipts"}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var tagId = await client.FindOrCreateTagAsync("Documents/Receipts/2026", TestContext.CancellationToken);

        Assert.AreEqual("tag-2026", tagId);
        Assert.HasCount(3, handler.Requests);
        using JsonDocument receiptsBody = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.AreEqual("Receipts", receiptsBody.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("tag-root", receiptsBody.RootElement.GetProperty("parentId").GetString());
        using JsonDocument yearBody = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.AreEqual("2026", yearBody.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("tag-receipts", yearBody.RootElement.GetProperty("parentId").GetString());
    }

    [TestMethod]
    [DataRow("Documents//Receipts")]
    [DataRow("/Documents")]
    [DataRow("Documents/ ")]
    public async Task Rejects_invalid_tag_path_segments_before_sending_any_request(string path)
    {
        StubHttpMessageHandler handler = new((_, _) =>
            throw new InvalidOperationException("Invalid tag paths must be rejected before network access."));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.FindOrCreateTagAsync(path, TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
        Assert.Contains("non-empty segments", exception.Message);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task Treats_duplicate_membership_as_already_applied()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubHttpMessageHandler.Json(
            """[{"id":"asset-8","success":false,"error":"duplicate"}]""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var added = await client.AddToAlbumAsync("album/7", "asset-8", TestContext.CancellationToken);

        Assert.IsFalse(added);
        Assert.AreEqual(HttpMethod.Put, handler.Requests.Single().Method);
        Assert.AreEqual("/api/albums/album%2F7/assets", handler.Requests.Single().Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.AreEqual("asset-8", body.RootElement.GetProperty("ids")[0].GetString());
    }

    [TestMethod]
    [DataRow("""[{"id":"asset-8"}]""", "IncompatibleApi")]
    [DataRow("""[{"id":"other","success":true}]""", "IncompatibleApi")]
    [DataRow("""[{"id":"asset-8","success":false,"error":"rejected"}]""", "IncompatibleApi")]
    [DataRow("""[{"id":"asset-8","success":false,"error":"no_permission"}]""", "Authentication")]
    [DataRow("{}", "IncompatibleApi")]
    [DataRow("[]", "IncompatibleApi")]
    public async Task Rejects_malformed_or_failed_membership_item(
        string responseJson,
        string expectedFailure)
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubHttpMessageHandler.Json(responseJson)));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.ApplyTagAsync("tag-3", "asset-8", TestContext.CancellationToken));

        Assert.AreEqual(Enum.Parse<ImmichApiFailureKind>(expectedFailure), exception.Kind);
        Assert.AreEqual(HttpMethod.Put, handler.Requests.Single().Method);
        Assert.AreEqual("/api/tags/tag-3/assets", handler.Requests.Single().Uri.AbsolutePath);
    }

    [TestMethod]
    public async Task Archive_requires_http_204_and_sends_only_the_requested_asset_id()
    {
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Method == HttpMethod.Put
                ? StubHttpMessageHandler.Json("{}", HttpStatusCode.OK)
                : throw new InvalidOperationException("Unexpected request.")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.ArchiveAsync("asset-8", TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Put, request.Method);
        Assert.AreEqual("/api/assets", request.Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.AreEqual("archive", body.RootElement.GetProperty("visibility").GetString());
        Assert.AreEqual("asset-8", body.RootElement.GetProperty("ids")[0].GetString());
        Assert.IsFalse(handler.Requests.Any(static item => item.Method == HttpMethod.Delete));
    }

    [TestMethod]
    public async Task Supported_membership_and_archive_mutations_never_use_DELETE()
    {
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Uri.AbsolutePath == "/api/assets"
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : StubHttpMessageHandler.Json("""[{"id":"asset-8","success":true}]""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = CreateClient(httpClient);

        var albumAdded = await client.AddToAlbumAsync("album-1", "asset-8", TestContext.CancellationToken);
        var tagApplied = await client.ApplyTagAsync("tag-1", "asset-8", TestContext.CancellationToken);
        await client.ArchiveAsync("asset-8", TestContext.CancellationToken);

        Assert.IsTrue(albumAdded);
        Assert.IsTrue(tagApplied);
        Assert.AreSequenceEqual([HttpMethod.Put, HttpMethod.Put, HttpMethod.Put],
            handler.Requests.Select(static request => request.Method).ToArray());
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Delete));
        Assert.AreSequenceEqual(
            ["/api/albums/album-1/assets", "/api/tags/tag-1/assets", "/api/assets"],
            handler.Requests.Select(static request => request.Uri.AbsolutePath).ToArray());
    }

    [TestMethod]
    public async Task Does_not_retry_mutations_after_network_or_server_failures()
    {
        StubHttpMessageHandler handler = new((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("network unavailable")));
        using ServiceProvider provider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.ArchiveAsync("asset-8", TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.Connectivity, exception.Kind);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task Does_not_retry_mutation_after_timeout_rejection()
    {
        StubHttpMessageHandler handler = new((_, _) =>
            Task.FromException<HttpResponseMessage>(new TimeoutRejectedException("simulated attempt timeout")));
        using ServiceProvider provider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.ArchiveAsync("asset-8", TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.Connectivity, exception.Kind);
        Assert.Contains("timed out", exception.Message);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(HttpMethod.Put, handler.Requests.Single().Method);
    }

    [TestMethod]
    public async Task Search_retries_network_failure_while_archive_remains_single_attempt()
    {
        var searchAttempts = 0;
        var archiveAttempts = 0;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Uri.AbsolutePath == "/api/search/metadata")
            {
                searchAttempts++;
                return searchAttempts == 1
                    ? Task.FromException<HttpResponseMessage>(new HttpRequestException("temporary"))
                    : Task.FromResult(StubHttpMessageHandler.Json("""{"assets":{"count":0,"items":[]}}"""));
            }

            archiveAttempts++;
            return Task.FromResult(StubHttpMessageHandler.Json("unavailable", HttpStatusCode.ServiceUnavailable));
        });
        using ServiceProvider provider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        ImmichApiClient client = CreateClient(httpClient);

        var page = await client.SearchMetadataAsync("receipt", 10, cancellationToken: TestContext.CancellationToken);
        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => client.ArchiveAsync("asset-8", TestContext.CancellationToken));

        Assert.AreEqual(0, page.AssetIds.Count);
        Assert.AreEqual(2, searchAttempts);
        Assert.AreEqual(1, archiveAttempts);
        Assert.AreEqual(ImmichApiFailureKind.Transient, exception.Kind);
        Assert.IsTrue(handler.Requests.All(static request => request.Method != HttpMethod.Delete));
    }

    [TestMethod]
    public async Task Search_scope_filters_apply_to_both_search_endpoints()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubHttpMessageHandler.Json(
            """{"assets":{"count":0,"items":[],"nextCursor":null,"nextPage":null}}""")));
        using HttpClient httpClient = new(handler);
        ImmichApiClient client = new(httpClient, "https://photos.example", "key", includeArchived: false, includeExternalLibraries: false);

        await client.SearchSmartAsync("receipt", 10, cancellationToken: TestContext.CancellationToken);
        await client.SearchMetadataAsync("invoice", 10, cancellationToken: TestContext.CancellationToken);

        Assert.HasCount(2, handler.Requests);
        foreach (var request in handler.Requests)
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            var filter = body.RootElement.GetProperty("filter");
            Assert.AreEqual("timeline", filter.GetProperty("visibility").GetProperty("eq").GetString());
            Assert.AreEqual(JsonValueKind.Null, filter.GetProperty("libraryId").GetProperty("eq").ValueKind);
        }
        Assert.AreEqual("/api/search/smart", handler.Requests[0].Uri.AbsolutePath);
        Assert.AreEqual("/api/search/metadata", handler.Requests[1].Uri.AbsolutePath);
    }

    private static ImmichApiClient CreateClient(HttpClient httpClient) =>
        new(httpClient, "https://photos.example", "key");

    private static ServiceProvider CreateResilienceServiceProvider(HttpMessageHandler handler)
    {
        ServiceCollection services = new();
        services.AddImmichHttpClient(() => handler);
        return services.BuildServiceProvider();
    }

    public TestContext TestContext { get; set; } = null!;
}
