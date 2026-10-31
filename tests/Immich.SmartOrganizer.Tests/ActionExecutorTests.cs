using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Immich.SmartOrganizer.Actions;
using Immich.SmartOrganizer.Immich;
using Immich.SmartOrganizer.Search;
using Immich.SmartOrganizer.State;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ActionExecutorTests
{
    private const string UserId = "user-1";
    private const string Marker = "2026-10-31T12:00:00Z";

    [TestMethod]
    [DataRow("user-2", "timeline")]
    [DataRow("user-1", "hidden")]
    [DataRow("user-1", "locked")]
    public async Task Refuses_foreign_or_protected_assets_before_mutating(
        string ownerId,
        string visibility)
    {
        using TemporaryDirectory directory = new();
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(AssetResponse("asset-1", ownerId, Marker, visibility)));
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);

        var result = await executor.ExecuteAsync(
            Plan(Action("asset-1", album: "Receipts", tag: null, archive: true)),
            [Asset("asset-1")], UserId, "rules-a", 5, 5, full: false, TestContext.CancellationToken);

        Assert.AreEqual(0, result.Mutations);
        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual(HttpMethod.Get, handler.Requests.Single().Method);
        Assert.AreEqual("/api/assets/asset-1", handler.Requests.Single().Uri.AbsolutePath);
        Assert.IsFalse(handler.Requests.Any(static request => request.Method != HttpMethod.Get));
        Assert.IsFalse(state.IsCompleted("asset-1", AssetFingerprint("rules-a", Asset("asset-1")), Marker));
    }

    [TestMethod]
    public async Task Resumes_partial_asset_without_repeating_confirmed_album_membership()
    {
        using TemporaryDirectory directory = new();
        var tagAttempts = 0;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Uri.AbsolutePath == "/api/assets/asset-1")
            {
                return Task.FromResult(AssetResponse("asset-1", UserId, Marker, "timeline"));
            }

            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/albums")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("[]"));
            }

            if (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/albums")
            {
                return Task.FromResult(StubHttpMessageHandler.Json(
                    """{"id":"album-1","albumName":"Receipts","albumUsers":[{"role":"owner","user":{"id":"user-1"}}]}"""));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/albums/album-1/assets")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("""[{"id":"asset-1","success":true}]"""));
            }

            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/tags")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("[]"));
            }

            if (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/tags")
            {
                using JsonDocument body = JsonDocument.Parse(request.Body!);
                var name = body.RootElement.GetProperty("name").GetString();
                return Task.FromResult(StubHttpMessageHandler.Json(
                    JsonSerializer.Serialize(new { id = "tag-" + name, name, parentId = (string?)null })));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath.StartsWith("/api/tags/", StringComparison.Ordinal))
            {
                tagAttempts++;
                return Task.FromResult(tagAttempts == 1
                    ? StubHttpMessageHandler.Json("failed", HttpStatusCode.ServiceUnavailable)
                    : StubHttpMessageHandler.Json("""[{"id":"asset-1","success":true}]"""));
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}.");
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);
        var plan = Plan(Action("asset-1", "Receipts", "Receipts", archive: false));
        var assets = new[] { Asset("asset-1") };

        var firstRun = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 5, 5, false, TestContext.CancellationToken);
        var secondRun = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 5, 5, false, TestContext.CancellationToken);

        Assert.AreEqual(1, firstRun.Errors.Count);
        Assert.AreEqual(1, firstRun.AlbumAdditions);
        Assert.AreEqual(0, firstRun.TagApplications);
        Assert.AreEqual(0, secondRun.Errors.Count);
        Assert.AreEqual(0, secondRun.AlbumAdditions);
        Assert.AreEqual(1, secondRun.TagApplications);
        Assert.AreEqual(1, handler.Requests.Count(static request =>
            request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/albums/album-1/assets"));
        Assert.AreEqual(2, tagAttempts);
        Assert.IsTrue(state.IsActionCompleted("asset-1", "album:Receipts"));
        Assert.IsTrue(state.IsActionCompleted("asset-1", "tag:Receipts"));
        Assert.IsTrue(state.IsCompleted("asset-1", AssetFingerprint("rules-a", Asset("asset-1")), Marker));
    }

    [TestMethod]
    public async Task Confirms_archive_only_after_a_204_and_a_follow_up_visibility_read()
    {
        using TemporaryDirectory directory = new();
        var visibility = "timeline";
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/assets/asset-1")
            {
                return Task.FromResult(AssetResponse("asset-1", UserId, Marker, visibility));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/assets")
            {
                using JsonDocument body = JsonDocument.Parse(request.Body!);
                Assert.AreEqual("archive", body.RootElement.GetProperty("visibility").GetString());
                Assert.AreEqual("asset-1", body.RootElement.GetProperty("ids")[0].GetString());
                visibility = "archive";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}.");
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);

        var result = await executor.ExecuteAsync(
            Plan(Action("asset-1", null, null, archive: true)),
            [Asset("asset-1")], UserId, "rules-a", 1, 5, false, TestContext.CancellationToken);

        Assert.AreEqual(1, result.Mutations);
        Assert.AreEqual(1, result.Archives);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(4, handler.Requests.Count(static request =>
            request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/assets/asset-1"));
        Assert.AreEqual(1, handler.Requests.Count(static request =>
            request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/assets"));
        Assert.IsTrue(state.IsActionCompleted("asset-1", "archive"));
        Assert.IsTrue(state.IsCompleted("asset-1", AssetFingerprint("rules-a", Asset("asset-1")), Marker));
    }

    [TestMethod]
    public async Task Reconciles_an_archive_accepted_before_the_response_was_lost()
    {
        using TemporaryDirectory directory = new();
        var visibility = "timeline";
        var archiveAttempts = 0;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/assets/asset-1")
            {
                return Task.FromResult(AssetResponse("asset-1", UserId, Marker, visibility));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/assets")
            {
                archiveAttempts++;
                visibility = "archive";
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("response lost after server accepted archive"));
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}.");
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);
        var plan = Plan(Action("asset-1", null, null, archive: true));
        var assets = new[] { Asset("asset-1") };

        var firstRun = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 1, 5, false, TestContext.CancellationToken);
        var secondRun = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 1, 5, false, TestContext.CancellationToken);

        Assert.AreEqual(1, firstRun.Errors.Count);
        Assert.Contains("Could not connect to Immich while requesting assets.", firstRun.Errors.Single());
        Assert.AreEqual(0, secondRun.Errors.Count);
        Assert.AreEqual(0, secondRun.Archives);
        Assert.AreEqual(1, archiveAttempts);
        Assert.IsTrue(state.IsActionCompleted("asset-1", "archive"));
        Assert.IsTrue(state.IsCompleted("asset-1", AssetFingerprint("rules-a", Asset("asset-1")), Marker));
    }

    [TestMethod]
    public async Task Zero_archive_limit_defers_each_candidate_without_sending_archive_requests()
    {
        using TemporaryDirectory directory = new();
        StubHttpMessageHandler handler = new((request, _) =>
        {
            var id = request.Uri.AbsolutePath.Split('/').Last();
            return request.Method == HttpMethod.Get
                ? Task.FromResult(AssetResponse(id, UserId, Marker, "timeline"))
                : throw new InvalidOperationException($"Unexpected mutation for {id}.");
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);

        var result = await executor.ExecuteAsync(
            Plan(Action("asset-1", null, null, true), Action("asset-2", null, null, true)),
            [Asset("asset-1"), Asset("asset-2")], UserId, "rules-a", 0, 5, false, TestContext.CancellationToken);

        Assert.AreEqual(0, result.Mutations);
        Assert.AreEqual(2, result.ArchiveLimited);
        Assert.AreEqual(0, result.Archives);
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Put));
        Assert.IsFalse(state.IsCompleted("asset-1", AssetFingerprint("rules-a", Asset("asset-1")), Marker));
        Assert.IsFalse(state.IsCompleted("asset-2", AssetFingerprint("rules-a", Asset("asset-2")), Marker));
    }

    [TestMethod]
    public async Task One_archive_per_run_advances_pending_assets_in_order_without_starvation()
    {
        using TemporaryDirectory directory = new();
        Dictionary<string, string> visibility = new(StringComparer.Ordinal)
        {
            ["asset-1"] = "timeline",
            ["asset-2"] = "timeline",
            ["asset-3"] = "timeline"
        };
        List<string> archivedIds = [];
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath.StartsWith("/api/assets/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(request.Uri.AbsolutePath.Split('/').Last());
                return Task.FromResult(AssetResponse(id, UserId, Marker, visibility[id]));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/assets")
            {
                using JsonDocument body = JsonDocument.Parse(request.Body!);
                var id = body.RootElement.GetProperty("ids")[0].GetString()!;
                visibility[id] = "archive";
                archivedIds.Add(id);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}.");
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);
        var plan = Plan(Action("asset-1", null, null, true), Action("asset-2", null, null, true), Action("asset-3", null, null, true));
        var assets = new[] { Asset("asset-1"), Asset("asset-2"), Asset("asset-3") };

        var first = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 1, 5, false, TestContext.CancellationToken);
        var second = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 1, 5, false, TestContext.CancellationToken);
        var third = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 1, 5, false, TestContext.CancellationToken);

        Assert.AreEqual(1, first.Archives);
        Assert.AreEqual(2, first.ArchiveLimited);
        Assert.AreEqual(1, second.Archives);
        Assert.AreEqual(1, second.ArchiveLimited);
        Assert.AreEqual(1, third.Archives);
        Assert.AreEqual(0, third.ArchiveLimited);
        Assert.AreEqual(2, third.Skipped);
        Assert.AreSequenceEqual(["asset-1", "asset-2", "asset-3"], archivedIds);
        Assert.IsTrue(assets.All(asset =>
            state.IsCompleted(asset.Id, AssetFingerprint("rules-a", asset), Marker)));
    }

    [TestMethod]
    public async Task Stops_before_later_actions_after_the_error_threshold_is_reached()
    {
        using TemporaryDirectory directory = new();
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Uri.AbsolutePath == "/api/assets/asset-1")
            {
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("network unavailable"));
            }

            return Task.FromResult(AssetResponse("asset-1", UserId, Marker, "timeline"));
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);

        var result = await executor.ExecuteAsync(
            Plan(Action("asset-1", null, null, true), Action("asset-2", null, null, true)),
            [Asset("asset-1"), Asset("asset-2")], UserId, "rules-a", 2, 1, false, TestContext.CancellationToken);

        Assert.AreEqual(1, result.Errors.Count);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual("/api/assets/asset-1", handler.Requests.Single().Uri.AbsolutePath);
        Assert.AreEqual(0, result.Mutations);
        Assert.IsFalse(handler.Requests.Any(static request => request.Uri.AbsolutePath == "/api/assets/asset-2"));
    }

    [TestMethod]
    public async Task Marker_rules_change_and_full_mode_each_force_action_reevaluation()
    {
        using TemporaryDirectory directory = new();
        var marker = Marker;
        var membershipWrites = 0;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/assets/asset-1")
            {
                return Task.FromResult(AssetResponse("asset-1", UserId, marker, "timeline"));
            }

            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/albums")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("[]"));
            }

            if (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/albums")
            {
                return Task.FromResult(StubHttpMessageHandler.Json(
                    """{"id":"album-1","albumName":"Receipts","albumUsers":[{"role":"owner","user":{"id":"user-1"}}]}"""));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath == "/api/albums/album-1/assets")
            {
                membershipWrites++;
                return Task.FromResult(StubHttpMessageHandler.Json(membershipWrites == 1
                    ? """[{"id":"asset-1","success":true}]"""
                    : """[{"id":"asset-1","success":false,"error":"duplicate"}]"""));
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}.");
        });
        using HttpClient httpClient = new(handler);
        using ScanStateStore state = NewState(directory.Path);
        ActionExecutor executor = new(CreateClient(httpClient), state);
        var plan = Plan(Action("asset-1", "Receipts", null, false));
        var assets = new[] { Asset("asset-1") };

        var initial = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 5, 5, false, TestContext.CancellationToken);
        var unchanged = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 5, 5, false, TestContext.CancellationToken);
        marker = "2026-10-31T12:00:01Z";
        var changedMarker = await executor.ExecuteAsync(plan, assets, UserId, "rules-a", 5, 5, false, TestContext.CancellationToken);
        var changedRules = await executor.ExecuteAsync(plan, assets, UserId, "rules-b", 5, 5, false, TestContext.CancellationToken);
        var full = await executor.ExecuteAsync(plan, assets, UserId, "rules-b", 5, 5, true, TestContext.CancellationToken);

        Assert.AreEqual(1, initial.AlbumAdditions);
        Assert.AreEqual(1, unchanged.Skipped);
        Assert.AreEqual(0, changedMarker.AlbumAdditions);
        Assert.AreEqual(0, changedRules.AlbumAdditions);
        Assert.AreEqual(0, full.AlbumAdditions);
        Assert.AreEqual(4, membershipWrites);
        Assert.IsTrue(state.IsCompleted("asset-1", AssetFingerprint("rules-b", Asset("asset-1")), marker));
    }

    private static ScanStateStore NewState(string directory) =>
        new(Path.Combine(directory, "state.db"));

    private static ImmichApiClient CreateClient(HttpClient httpClient) =>
        new(httpClient, "https://photos.example", "key");

    private static HttpResponseMessage AssetResponse(string id, string ownerId, string updatedAt, string visibility) =>
        StubHttpMessageHandler.Json(JsonSerializer.Serialize(new
        {
            id,
            ownerId,
            updatedAt,
            visibility
        }));

    private static DryRunActionPlanner.ActionPlan Plan(params DryRunActionPlanner.PlannedAssetActions[] actions) =>
        new(actions, 0);

    private static DryRunActionPlanner.PlannedAssetActions Action(
        string assetId,
        string? album,
        string? tag,
        bool archive) =>
        new(assetId, "receipts", album, tag, archive, false);

    private static DiscoveredAsset Asset(string id) =>
        new(id, [new CandidateEvidence("receipts", SearchEvidenceKind.Ocr, "invoice")]);

    private static string AssetFingerprint(string fingerprint, DiscoveredAsset asset)
    {
        var evidence = JsonSerializer.Serialize(asset.Evidence
            .OrderBy(static item => item.Category, StringComparer.Ordinal)
            .ThenBy(static item => item.Kind)
            .ThenBy(static item => item.Query, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint + evidence)));
    }

    public TestContext TestContext { get; set; } = null!;
}
