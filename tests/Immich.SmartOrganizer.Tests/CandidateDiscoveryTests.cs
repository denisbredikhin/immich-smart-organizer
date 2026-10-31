using System.Net;
using System.Text.Json;
using Immich.SmartOrganizer.Configuration;
using Immich.SmartOrganizer.Immich;
using Immich.SmartOrganizer.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class CandidateDiscoveryTests
{
    [TestMethod]
    public async Task DiscoverAsync_aggregates_duplicates_and_preserves_search_evidence()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Response("""{"count":1,"items":[{"id":"asset-1"}],"nextCursor":null,"nextPage":null}"""),
            Response("""{"count":2,"items":[{"id":"asset-1"},{"id":"asset-2"}],"nextCursor":"page-2","nextPage":null}"""),
            Response("""{"count":2,"items":[{"id":"asset-2"},{"id":"asset-3"}],"nextCursor":null,"nextPage":null}""")
        ]);
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(responses.Dequeue()));
        using HttpClient httpClient = new(handler);
        CandidateDiscovery discovery = new(CreateClient(httpClient));
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "receipts",
            Search = new AppConfiguration.SearchOptions
            {
                Contextual = ["receipt"],
                Ocr = ["vat"]
            },
            Match = new()
        };

        var result = await discovery.DiscoverAsync([category], batchSize: 3, maxAssets: 10, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsPartial);
        Assert.HasCount(3, result.Assets);
        Assert.AreSequenceEqual(
            expected, result.Assets.Select(static asset => asset.Id).ToArray());
        var firstAssetEvidence = result.Assets[0].Evidence;
        Assert.HasCount(2, firstAssetEvidence);
        Assert.Contains(new CandidateEvidence("receipts", SearchEvidenceKind.Contextual, "receipt"), firstAssetEvidence);
        Assert.Contains(new CandidateEvidence("receipts", SearchEvidenceKind.Ocr, "vat"), firstAssetEvidence);
        Assert.Contains(new CandidateEvidence("receipts", SearchEvidenceKind.Ocr, "vat"), result.Assets[1].Evidence);

        using JsonDocument secondMetadataRequest = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.AreEqual(
            "page-2",
            secondMetadataRequest.RootElement.GetProperty("cursor").GetString());
    }

    [TestMethod]
    public async Task DiscoverAsync_stops_at_global_asset_limit_and_reports_partial_coverage()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(
            """{"count":1,"items":[{"id":"asset-1"}],"nextCursor":"more","nextPage":null}""")));
        using HttpClient httpClient = new(handler);
        CandidateDiscovery discovery = new(CreateClient(httpClient));
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "documents",
            Search = new AppConfiguration.SearchOptions
            {
                Ocr = ["invoice", "receipt"]
            },
            Match = new()
        };

        var result = await discovery.DiscoverAsync([category], batchSize: 10, maxAssets: 1, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsPartial);
        Assert.HasCount(1, result.Assets);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains(static reason => reason.Contains("asset limit", StringComparison.Ordinal), result.PartialReasons);
    }

    [TestMethod]
    public async Task DiscoverAsync_marks_full_smart_search_window_as_potentially_truncated()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(
            """{"count":2,"items":[{"id":"asset-1"},{"id":"asset-2"}],"nextCursor":null,"nextPage":null}""")));
        using HttpClient httpClient = new(handler);
        CandidateDiscovery discovery = new(CreateClient(httpClient));
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "tickets",
            Search = new AppConfiguration.SearchOptions { Contextual = ["boarding pass"] },
            Match = new()
        };

        var result = await discovery.DiscoverAsync([category], batchSize: 2, maxAssets: 20, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsPartial);
        Assert.HasCount(2, result.Assets);
        Assert.Contains(static reason => reason.Contains("no supported continuation", StringComparison.Ordinal), result.PartialReasons);
    }

    [TestMethod]
    public async Task DiscoverAsync_stops_when_immich_repeats_a_metadata_cursor()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Response("""{"count":1,"items":[{"id":"asset-1"}],"nextCursor":"same-cursor","nextPage":null}"""),
            Response("""{"count":1,"items":[{"id":"asset-2"}],"nextCursor":"same-cursor","nextPage":null}""")
        ]);
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(responses.Dequeue()));
        using HttpClient httpClient = new(handler);
        CandidateDiscovery discovery = new(CreateClient(httpClient));
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "documents",
            Search = new AppConfiguration.SearchOptions { Ocr = ["invoice"] },
            Match = new()
        };

        var exception = await Assert.ThrowsExactlyAsync<ImmichApiException>(
            () => discovery.DiscoverAsync([category], batchSize: 1, maxAssets: 20, cancellationToken: TestContext.CancellationToken));

        Assert.AreEqual(ImmichApiFailureKind.IncompatibleApi, exception.Kind);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task DiscoverAsync_continues_after_retryable_query_failure_and_marks_partial()
    {
        StubHttpMessageHandler handler = new((request, _) =>
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            var query = body.RootElement.GetProperty("filter").GetProperty("ocr").GetProperty("matches").GetString();
            return Task.FromResult(query == "broken"
                ? Response("temporarily unavailable", HttpStatusCode.ServiceUnavailable)
                : Response("""{"count":1,"items":[{"id":"asset-2"}],"nextCursor":null,"nextPage":null}"""));
        });
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        CandidateDiscovery discovery = new(CreateClient(httpClient));
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "documents",
            Search = new AppConfiguration.SearchOptions { Ocr = ["broken", "receipt"] },
            Match = new()
        };

        var result = await discovery.DiscoverAsync([category], batchSize: 5, maxAssets: 20, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsPartial);
        Assert.HasCount(1, result.Assets);
        Assert.AreEqual("asset-2", result.Assets[0].Id);
        Assert.HasCount(4, handler.Requests);
        Assert.Contains(static reason => reason.Contains("bounded retries", StringComparison.Ordinal), result.PartialReasons);
    }

    [TestMethod]
    public async Task DiscoverAsync_stops_issuing_searches_when_maximum_error_threshold_is_reached()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            Response("temporarily unavailable", HttpStatusCode.ServiceUnavailable)));
        using ServiceProvider serviceProvider = CreateResilienceServiceProvider(handler);
        using HttpClient httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ImmichHttpClientRegistration.ClientName);
        CandidateDiscovery discovery = new(CreateClient(httpClient), maxErrors: 1);
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "documents",
            Search = new AppConfiguration.SearchOptions { Ocr = ["broken", "must-not-run"] },
            Match = new()
        };

        var result = await discovery.DiscoverAsync(
            [category], batchSize: 5, maxAssets: 20, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsPartial);
        Assert.IsEmpty(result.Assets);
        Assert.HasCount(3, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request =>
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            return body.RootElement.GetProperty("filter").GetProperty("ocr")
                .GetProperty("matches").GetString() == "broken";
        }));
        Assert.Contains(static reason => reason.Contains("maxErrorsPerRun", StringComparison.Ordinal), result.PartialReasons);
    }

    [TestMethod]
    public async Task DiscoverAsync_skips_disabled_categories_without_requests()
    {
        StubHttpMessageHandler handler = new((_, _) =>
            throw new InvalidOperationException("Disabled categories must not be searched."));
        using HttpClient httpClient = new(handler);
        CandidateDiscovery discovery = new(CreateClient(httpClient));
        AppConfiguration.CategoryOptions category = new()
        {
            Name = "disabled",
            Enabled = false,
            Search = new AppConfiguration.SearchOptions
            {
                Contextual = ["receipt"],
                Ocr = ["invoice"]
            },
            Match = new()
        };

        var result = await discovery.DiscoverAsync([category], batchSize: 5, maxAssets: 20, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(result.IsPartial);
        Assert.IsEmpty(result.Assets);
        Assert.IsEmpty(handler.Requests);
    }

    private static ImmichApiClient CreateClient(HttpClient httpClient) =>
        new(httpClient, "http://localhost:2283", "test-api-key");

    private static ServiceProvider CreateResilienceServiceProvider(HttpMessageHandler handler)
    {
        ServiceCollection services = new();
        services.AddImmichHttpClient(() => handler);
        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage Response(
        string assetsJson,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        StubHttpMessageHandler.Json(
            statusCode == HttpStatusCode.OK
                ? $$"""{"assets":{{assetsJson}}}"""
                : assetsJson,
            statusCode);

    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] expected = new[] { "asset-1", "asset-2", "asset-3" };
}
