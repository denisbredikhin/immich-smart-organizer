using System.Net;
using Immich.SmartOrganizer.Cli;
using Immich.SmartOrganizer.Immich;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class CommandLineApplicationTests
{
    [TestMethod]
    [DataRow("--help")]
    [DataRow("-h")]
    public async Task Help_options_print_usage_and_return_success(string argument)
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, argument);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("immich-smart-organizer config validate [--config PATH]", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task No_arguments_print_usage_and_return_success()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    [DataRow("--version")]
    [DataRow("-v")]
    public async Task Version_options_print_package_version_and_return_success(string argument)
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, argument);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("0.1.0-alpha.1" + Environment.NewLine, result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task Unknown_argument_prints_diagnostic_and_returns_invalid_arguments()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, "--unknown");

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.Contains("unsupported argument '--unknown'", result.Error);
        Assert.Contains("Use --help", result.Error);
    }

    [TestMethod]
    public async Task Init_writes_valid_default_configuration()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["init"]);
        var configurationPath = Path.Combine(directory.Path, "immich-smart-organizer.yaml");

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(File.Exists(configurationPath));
        var yaml = await File.ReadAllTextAsync(configurationPath, TestContext.CancellationToken);
        Assert.Contains("dryRun: true", yaml);
        Assert.Contains("archive: false", yaml);
        Assert.IsFalse(yaml.Contains("apiKey:", StringComparison.Ordinal));

        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };
        var validation = await ExecuteAsync(
            directory.Path,
            ["config", "validate"],
            environmentVariables);

        Assert.AreEqual(0, validation.ExitCode);
        Assert.Contains("Configuration is valid.", validation.Output);
        Assert.IsFalse(validation.Output.Contains("test-api-key", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, validation.Error);
    }

    [TestMethod]
    public async Task Init_writes_configuration_to_explicit_output_path()
    {
        using TemporaryDirectory directory = new();
        var outputDirectory = Path.Combine(directory.Path, "output");
        Directory.CreateDirectory(outputDirectory);
        var result = await ExecuteAsync(directory.Path, ["init", "--output", "output/custom.yaml"]);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "custom.yaml")));
        Assert.Contains(Path.Combine(outputDirectory, "custom.yaml"), result.Output);
    }

    [TestMethod]
    public async Task Init_rejects_output_path_with_missing_parent_directory()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["init", "--output", "missing/custom.yaml"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("output directory does not exist", result.Error);
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "missing")));
    }

    [TestMethod]
    public async Task Init_rejects_unsupported_options()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["init", "--unknown"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("usage: init [--output PATH]", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Init_refuses_to_overwrite_existing_file()
    {
        using TemporaryDirectory directory = new();
        const string existingContent = "preserve this file";
        var configurationPath = Path.Combine(directory.Path, "immich-smart-organizer.yaml");
        await File.WriteAllTextAsync(configurationPath, existingContent, TestContext.CancellationToken);

        var result = await ExecuteAsync(directory.Path, ["init"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(existingContent, await File.ReadAllTextAsync(configurationPath, TestContext.CancellationToken));
        Assert.Contains("refusing to overwrite", result.Error);
    }

    [TestMethod]
    public async Task Config_validate_uses_explicit_config_path()
    {
        using TemporaryDirectory directory = new();
        var configDirectory = Path.Combine(directory.Path, "configs");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "explicit.yaml"),
            TestConfiguration.ValidYaml, TestContext.CancellationToken);
        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };

        var result = await ExecuteAsync(
            directory.Path,
            ["config", "validate", "--config", Path.Combine("configs", "explicit.yaml")],
            environmentVariables);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains(Path.Combine(configDirectory, "explicit.yaml"), result.Output);
        Assert.IsFalse(result.Output.Contains("test-api-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Config_validate_suggests_init_when_default_config_is_missing()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["config", "validate"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("Run 'immich-smart-organizer init' to create one.", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Config_validate_rejects_missing_config_path()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["config", "validate", "--config"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("usage: config validate [--config PATH]", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Config_validate_rejects_duplicate_config_paths()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(
            directory.Path,
            ["config", "validate", "--config", "first.yaml", "--config", "second.yaml"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("usage: config validate [--config PATH]", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("100001")]
    public async Task Run_rejects_scan_limit_outside_allowed_range(string limit)
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", "--limit", limit]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("between 1 and 100000", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    [DataRow("--config", "requires a path")]
    [DataRow("--limit", "must be an integer between 1 and 100000")]
    [DataRow("--category", "requires a name")]
    public async Task Run_rejects_scan_options_without_values(string option, string expectedMessage)
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", option]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains($"option '{option}' {expectedMessage}", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    [DataRow("not-a-number")]
    [DataRow("-1")]
    public async Task Run_rejects_non_integer_scan_limits(string limit)
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", "--limit", limit]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("must be an integer between 1 and 100000", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Run_rejects_duplicate_scan_options()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", "--full", "--full"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("cannot be specified more than once", result.Error);
    }

    [TestMethod]
    public async Task Run_rejects_unknown_scan_option()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", "--unknown"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("unsupported scan option '--unknown'", result.Error);
    }

    [TestMethod]
    public async Task Scan_checks_version_then_discovers_deduplicated_candidates_without_mutations()
    {
        using TemporaryDirectory directory = new();
        var configurationPath = Path.Combine(directory.Path, "scan.yaml");
        await File.WriteAllTextAsync(configurationPath, TestConfiguration.ScanYaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) =>
        {
            var response = request.Uri.AbsolutePath switch
            {
                "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
                "/api/search/smart" => StubHttpMessageHandler.Json(SearchResponse("asset-1", "asset-2")),
                "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1", "asset-3")),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml", "--category", "receipts"],
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["IMMICH_API_KEY"] = "test-api-key"
            },
            handler);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Error);
        Assert.Contains("Immich version: 3.3.5", result.Output);
        Assert.Contains("Candidates discovered: 3", result.Output);
        Assert.Contains("Assets evaluated: 3", result.Output);
        Assert.Contains("receipts: 2", result.Output);
        Assert.Contains("Album additions planned: 0", result.Output);
        Assert.Contains("Archivals planned: 0", result.Output);
        Assert.Contains("Mutations executed: 0", result.Output);
        Assert.Contains("Coverage: search windows completed", result.Output);
        Assert.Contains("No library changes were made.", result.Output);
        Assert.AreSequenceEqual(
            expected, handler.Requests.Select(static request => request.Uri.AbsolutePath).ToArray());
        Assert.IsFalse(handler.Requests[0].Headers.ContainsKey("x-api-key"));
        Assert.IsTrue(handler.Requests.Skip(1).All(static request =>
            request.Headers.TryGetValue("x-api-key", out var values)
            && values.SequenceEqual(["test-api-key"])));
        Assert.Contains("\"query\":\"receipt\"", handler.Requests[1].Body!);
        Assert.Contains("\"filter\":{\"type\":{\"eq\":\"IMAGE\"}}", handler.Requests[1].Body!);
        Assert.Contains("\"matches\":\"invoice\"", handler.Requests[2].Body!);
        Assert.Contains("\"type\":{\"eq\":\"IMAGE\"}", handler.Requests[2].Body!);
        Assert.IsTrue(handler.Requests.All(static request =>
            request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Post
                && request.Uri.AbsolutePath.StartsWith("/api/search/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Scan_dry_run_plans_archive_without_sending_mutation_requests()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ScanYaml
            .Replace("dryRun: true", "dryRun: false", StringComparison.Ordinal)
            .Replace("archive: false", "archive: true", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), yaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) =>
        {
            var response = request.Uri.AbsolutePath switch
            {
                "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
                "/api/search/smart" or "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1")),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml", "--dry-run", "--verbose"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            handler);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("Dry run: true", result.Output);
        Assert.Contains("Archivals planned: 1", result.Output);
        Assert.Contains("Mutations executed: 0", result.Output);
        Assert.Contains("matched category 'receipts' using ocrAny=invoice", result.Output);
        Assert.IsTrue(handler.Requests.All(static request =>
            request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Post
                && request.Uri.AbsolutePath.StartsWith("/api/search/", StringComparison.Ordinal)));
        Assert.HasCount(3, handler.Requests);
        Assert.IsFalse(File.Exists(Path.Combine(directory.Path, "state.db")));
        Assert.IsFalse(File.Exists(Path.Combine(directory.Path, "state.db.lock")));
    }

    [TestMethod]
    public async Task Scan_default_dry_run_does_not_create_state_or_send_mutations()
    {
        using TemporaryDirectory directory = new();
        var yaml = LiveArchiveYaml.Replace("dryRun: false", "dryRun: true", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), yaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = SearchAndVersionHandler();

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            ApiEnvironment(),
            handler);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("Dry run: true", result.Output);
        Assert.Contains("Archivals planned: 1", result.Output);
        Assert.Contains("Mutations executed: 0", result.Output);
        Assert.IsTrue(handler.Requests.All(IsReadOnlyRequest));
        Assert.IsFalse(File.Exists(Path.Combine(directory.Path, "state.db")));
        Assert.IsFalse(File.Exists(Path.Combine(directory.Path, "state.db.lock")));
    }

    [TestMethod]
    [DataRow("contextual")]
    [DataRow("ocr")]
    public async Task Scan_requires_both_contextual_and_ocr_match_gates_for_live_archive(string missingGate)
    {
        using TemporaryDirectory directory = new();
        var yaml = LiveArchiveYaml.Replace(
            missingGate == "contextual"
                ? "- contextualAny: [receipt]"
                : "- ocrAny: [invoice]",
            string.Empty,
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), yaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((_, _) =>
            throw new InvalidOperationException("Unsafe live archive configuration must fail before API access."));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            ApiEnvironment(),
            handler);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("Live archival requires both contextualAny and ocrAny/ocrAll", result.Error);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task Scan_partial_discovery_blocks_all_live_mutations()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), LiveArchiveYaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.Uri.AbsolutePath == "/api/server/version")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""));
            }

            if (request.Uri.AbsolutePath == "/api/search/smart")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("unavailable", HttpStatusCode.ServiceUnavailable));
            }

            if (request.Uri.AbsolutePath == "/api/search/metadata")
            {
                return Task.FromResult(StubHttpMessageHandler.Json(SearchResponse("asset-1")));
            }

            throw new InvalidOperationException($"Partial discovery must not execute {request.Method} {request.Uri}.");
        });

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            ApiEnvironment(),
            handler);

        Assert.AreEqual(5, result.ExitCode);
        Assert.Contains("Coverage: partial", result.Output);
        Assert.Contains("Execution: blocked because discovery was partial.", result.Output);
        Assert.Contains("Mutations executed: 0", result.Output);
        Assert.AreEqual(5, handler.Requests.Count);
        Assert.IsFalse(handler.Requests.Any(static request => request.Uri.AbsolutePath == "/api/users/me"));
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Put));
    }

    [TestMethod]
    public async Task Scan_error_threshold_stops_later_searches_before_live_actions()
    {
        using TemporaryDirectory directory = new();
        var yaml = LiveArchiveYaml.Replace("maxErrorsPerRun: 5", "maxErrorsPerRun: 1", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), yaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Uri.AbsolutePath == "/api/server/version"
                ? StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}""")
                : StubHttpMessageHandler.Json("unavailable", HttpStatusCode.ServiceUnavailable)));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            ApiEnvironment(),
            handler);

        Assert.AreEqual(5, result.ExitCode);
        Assert.Contains("maxErrorsPerRun stopped remaining search requests", result.Output);
        Assert.AreEqual(4, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.Skip(1).All(static request => request.Uri.AbsolutePath == "/api/search/smart"));
        Assert.IsFalse(handler.Requests.Any(static request => request.Uri.AbsolutePath == "/api/search/metadata"));
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Put));
        Assert.IsFalse(handler.Requests.Any(static request => request.Uri.AbsolutePath == "/api/users/me"));
    }

    [TestMethod]
    public async Task Live_scan_composes_http_and_sqlite_then_skips_unchanged_completed_asset()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), LiveArchiveYaml, TestContext.CancellationToken);
        var visibility = "timeline";
        StubHttpMessageHandler FirstHandler() => new((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
            "/api/search/smart" or "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1")),
            "/api/users/me" => StubHttpMessageHandler.Json("""{"id":"user-1"}"""),
            "/api/assets/asset-1" => AssetResponse("asset-1", "user-1", "2026-10-31T12:00:00Z", visibility),
            "/api/assets" when request.Method == HttpMethod.Put => ArchiveAndReturnNoContent(() => visibility = "archive"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var firstHandler = FirstHandler();

        var first = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml"], ApiEnvironment(), firstHandler);

        Assert.AreEqual(0, first.ExitCode);
        Assert.Contains("Archivals confirmed: 1", first.Output);
        Assert.Contains("Mutations executed: 1", first.Output);
        Assert.AreEqual(1, firstHandler.Requests.Count(static request => request.Method == HttpMethod.Put));
        Assert.IsTrue(File.Exists(Path.Combine(directory.Path, "state.db")));

        var secondHandler = new StubHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
            "/api/search/smart" or "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1")),
            "/api/users/me" => StubHttpMessageHandler.Json("""{"id":"user-1"}"""),
            "/api/assets/asset-1" => AssetResponse("asset-1", "user-1", "2026-10-31T12:00:00Z", visibility),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));

        var second = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml"], ApiEnvironment(), secondHandler);

        Assert.AreEqual(0, second.ExitCode);
        Assert.Contains("Unchanged completed assets skipped: 1", second.Output);
        Assert.Contains("Archivals confirmed: 0", second.Output);
        Assert.AreEqual(0, secondHandler.Requests.Count(static request => request.Method == HttpMethod.Put));
        Assert.AreEqual(1, secondHandler.Requests.Count(static request =>
            request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/assets/asset-1"));
    }

    [TestMethod]
    public async Task Live_scan_rechecks_asset_marker_rule_changes_and_full_mode()
    {
        using TemporaryDirectory directory = new();
        var marker = "2026-10-31T12:00:00Z";
        var membershipWrites = 0;
        Dictionary<string, string> albums = new(StringComparer.Ordinal);
        StubHttpMessageHandler CreateHandler() => new((request, _) =>
        {
            if (request.Uri.AbsolutePath == "/api/server/version")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""));
            }

            if (request.Uri.AbsolutePath is "/api/search/smart" or "/api/search/metadata")
            {
                return Task.FromResult(StubHttpMessageHandler.Json(SearchResponse("asset-1")));
            }

            if (request.Uri.AbsolutePath == "/api/users/me")
            {
                return Task.FromResult(StubHttpMessageHandler.Json("""{"id":"user-1"}"""));
            }

            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/assets/asset-1")
            {
                return Task.FromResult(AssetResponse("asset-1", "user-1", marker, "timeline"));
            }

            if (request.Method == HttpMethod.Get && request.Uri.AbsolutePath == "/api/albums")
            {
                var name = Uri.UnescapeDataString(request.Uri.Query.Split("name=").Last());
                var response = albums.TryGetValue(name, out var albumId)
                    ? System.Text.Json.JsonSerializer.Serialize(new[]
                    {
                        new
                        {
                            id = albumId,
                            albumName = name,
                            albumUsers = new[] { new { role = "owner", user = new { id = "user-1" } } }
                        }
                    })
                    : "[]";
                return Task.FromResult(StubHttpMessageHandler.Json(response));
            }

            if (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/api/albums")
            {
                using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(request.Body!);
                var name = body.RootElement.GetProperty("albumName").GetString()!;
                var albumId = "album-" + name.ToLowerInvariant();
                albums[name] = albumId;
                return Task.FromResult(StubHttpMessageHandler.Json(System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = albumId,
                    albumName = name,
                    albumUsers = new[] { new { role = "owner", user = new { id = "user-1" } } }
                })));
            }

            if (request.Method == HttpMethod.Put && request.Uri.AbsolutePath.StartsWith("/api/albums/", StringComparison.Ordinal))
            {
                membershipWrites++;
                using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(request.Body!);
                var assetId = body.RootElement.GetProperty("ids")[0].GetString();
                return Task.FromResult(StubHttpMessageHandler.Json(membershipWrites == 1
                    ? System.Text.Json.JsonSerializer.Serialize(new[] { new { id = assetId, success = true } })
                    : System.Text.Json.JsonSerializer.Serialize(new[] { new { id = assetId, success = false, error = "duplicate" } })));
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.Uri}.");
        });

        var configPath = Path.Combine(directory.Path, "scan.yaml");
        await File.WriteAllTextAsync(configPath, LiveAlbumYaml("Receipts"), TestContext.CancellationToken);
        var firstHandler = CreateHandler();
        var first = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml"], ApiEnvironment(), firstHandler);
        var unchangedHandler = CreateHandler();
        var unchanged = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml"], ApiEnvironment(), unchangedHandler);

        marker = "2026-10-31T12:00:01Z";
        var markerHandler = CreateHandler();
        var markerChanged = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml"], ApiEnvironment(), markerHandler);

        await File.WriteAllTextAsync(configPath, LiveAlbumYaml("Invoices"), TestContext.CancellationToken);
        var rulesHandler = CreateHandler();
        var rulesChanged = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml"], ApiEnvironment(), rulesHandler);
        var fullHandler = CreateHandler();
        var full = await ExecuteAsync(directory.Path, ["scan", "--config", "scan.yaml", "--full"], ApiEnvironment(), fullHandler);

        Assert.AreEqual(0, first.ExitCode);
        Assert.Contains("Album additions confirmed: 1", first.Output);
        Assert.AreEqual(0, unchanged.ExitCode);
        Assert.Contains("Unchanged completed assets skipped: 1", unchanged.Output);
        Assert.AreEqual(0, unchangedHandler.Requests.Count(static request =>
            request.Method == HttpMethod.Put && request.Uri.AbsolutePath.StartsWith("/api/albums/", StringComparison.Ordinal)));
        Assert.AreEqual(0, markerChanged.ExitCode);
        Assert.AreEqual(1, markerHandler.Requests.Count(static request =>
            request.Method == HttpMethod.Put && request.Uri.AbsolutePath.StartsWith("/api/albums/", StringComparison.Ordinal)));
        Assert.AreEqual(0, rulesChanged.ExitCode);
        Assert.AreEqual(1, rulesHandler.Requests.Count(static request =>
            request.Method == HttpMethod.Put && request.Uri.AbsolutePath.StartsWith("/api/albums/", StringComparison.Ordinal)));
        Assert.AreEqual(0, full.ExitCode);
        Assert.Contains("Scan mode: full re-evaluation of discoverable candidates", full.Output);
        Assert.AreEqual(1, fullHandler.Requests.Count(static request =>
            request.Method == HttpMethod.Put && request.Uri.AbsolutePath.StartsWith("/api/albums/", StringComparison.Ordinal)));
        Assert.AreEqual(4, membershipWrites);
        Assert.IsFalse(firstHandler.Requests.Any(static request => request.Method == HttpMethod.Delete));
    }

    [TestMethod]
    public async Task Category_filter_does_not_hide_cross_category_ambiguity()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), AmbiguousLiveYaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
            "/api/search/smart" or "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1")),
            "/api/users/me" => StubHttpMessageHandler.Json("""{"id":"user-1"}"""),
            _ => throw new InvalidOperationException($"Ambiguous candidates must not mutate: {request.Method} {request.Uri}.")
        }));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml", "--category", "receipts"],
            ApiEnvironment(),
            handler);

        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("Needs review: 1", result.Output);
        Assert.Contains("Ambiguous candidates skipped: 0", result.Output);
        Assert.Contains("Album additions planned: 0", result.Output);
        Assert.AreEqual(2, handler.Requests.Count(static request => request.Uri.AbsolutePath.StartsWith("/api/search/", StringComparison.Ordinal)));
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Put));
        Assert.IsFalse(handler.Requests.Any(static request => request.Method == HttpMethod.Post && !request.Uri.AbsolutePath.StartsWith("/api/search/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Scan_rejects_invalid_tag_hierarchy_before_contacting_Immich()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "tag: Documents/Receipts",
            "tag: Documents//Receipts",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), yaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((_, _) =>
            throw new InvalidOperationException("Invalid tag hierarchy must be rejected before API requests."));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            handler);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("categories[0].actions.tag must contain non-empty hierarchy segments", result.Error);
        Assert.IsEmpty(handler.Requests);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Scan_rejects_immich_url_with_fragment_as_configuration_error()
    {
        using TemporaryDirectory directory = new();
        var scanYaml = TestConfiguration.ScanYaml.Replace(
            "http://localhost:2283",
            "http://localhost:2283/#/photos",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), scanYaml, TestContext.CancellationToken);
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Invalid configuration must not send requests."));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            handler);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("Configuration error:", result.Error);
        Assert.Contains("without embedded credentials, query, or fragment", result.Error);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task Scan_rejects_unsupported_server_before_searching()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), TestConfiguration.ScanYaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"major":3,"minor":1,"patch":9,"prerelease":0}""")));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            handler);

        Assert.AreEqual(4, result.ExitCode);
        Assert.Contains("is not supported", result.Error);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual("/api/server/version", handler.Requests[0].Uri.AbsolutePath);
    }

    [TestMethod]
    public async Task Scan_maps_authentication_failure_to_exit_code_three()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), TestConfiguration.ScanYaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Uri.AbsolutePath == "/api/server/version"
                ? StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}""")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            handler);

        Assert.AreEqual(3, result.ExitCode);
        Assert.Contains("authentication or permission failure", result.Error);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Scan_returns_partial_exit_code_when_a_query_exhausts_retries()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), TestConfiguration.ScanYaml, TestContext.CancellationToken);
        StubHttpMessageHandler handler = new((request, _) =>
        {
            var response = request.Uri.AbsolutePath switch
            {
                "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
                "/api/search/smart" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1")),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            handler);

        Assert.AreEqual(5, result.ExitCode);
        Assert.Contains("Candidates discovered: 1", result.Output);
        Assert.Contains("Coverage: partial", result.Output);
        Assert.Contains("failed after bounded retries", result.Output);
        Assert.HasCount(5, handler.Requests);
    }

    [TestMethod]
    public async Task Run_rejects_unconfigured_category()
    {
        using TemporaryDirectory directory = new();
        var configurationPath = Path.Combine(directory.Path, "scan.yaml");
        await File.WriteAllTextAsync(configurationPath, TestConfiguration.ValidYaml, TestContext.CancellationToken);
        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml", "--category", "missing"],
            environmentVariables);

        Assert.AreEqual(2, result.ExitCode);
        Assert.Contains("category 'missing' is not configured", result.Error);
        Assert.AreEqual(string.Empty, result.Output);
    }

    private static async Task<CommandResult> ExecuteAsync(
        string currentDirectory,
        params string[] args)
    {
        return await ExecuteAsync(currentDirectory, args, new Dictionary<string, string?>());
    }

    private static async Task<CommandResult> ExecuteAsync(
        string currentDirectory,
        string[] args,
        IReadOnlyDictionary<string, string?> environmentVariables,
        HttpMessageHandler? httpMessageHandler = null)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        TestCommandEnvironment environment = new(currentDirectory, environmentVariables);

        ServiceCollection services = new();
        services.AddApplicationServices();
        services.Replace(ServiceDescriptor.Singleton<ICommandEnvironment>(environment));
        if (httpMessageHandler is not null)
        {
            services.Configure<HttpClientFactoryOptions>(
                ImmichHttpClientRegistration.ClientName,
                options => options.HttpMessageHandlerBuilderActions.Add(
                    builder => builder.PrimaryHandler = httpMessageHandler));
        }

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var application = serviceProvider.GetRequiredService<CommandLineApplication>();
        var exitCode = await application.RunAsync(args, output, error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private static string SearchResponse(params string[] assetIds) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            assets = new
            {
                count = assetIds.Length,
                items = assetIds.Select(static id => new { id })
            }
        });

    private static HttpResponseMessage AssetResponse(
        string id,
        string ownerId,
        string updatedAt,
        string visibility) =>
        StubHttpMessageHandler.Json(System.Text.Json.JsonSerializer.Serialize(new
        {
            id,
            ownerId,
            updatedAt,
            visibility
        }));

    private static HttpResponseMessage ArchiveAndReturnNoContent(Action archive)
    {
        archive();
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static StubHttpMessageHandler SearchAndVersionHandler() =>
        new((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/server/version" => StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}"""),
            "/api/search/smart" or "/api/search/metadata" => StubHttpMessageHandler.Json(SearchResponse("asset-1")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));

    private static IReadOnlyDictionary<string, string?> ApiEnvironment() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };

    private static bool IsReadOnlyRequest(CapturedHttpRequest request) =>
        request.Method == HttpMethod.Get
        || request.Method == HttpMethod.Post
            && request.Uri.AbsolutePath.StartsWith("/api/search/", StringComparison.Ordinal);

    private static string LiveAlbumYaml(string albumName) => $$"""
        immich:
          url: "http://localhost:2283"
          apiKeyEnv: IMMICH_API_KEY
        scanner:
          batchSize: 10
        processing:
          dryRun: false
          maxAssetsPerRun: 20
          maxErrorsPerRun: 5
          ambiguousAssetPolicy: review
        state:
          path: ./state.db
        categories:
          - name: receipts
            search:
              contextual: [receipt]
            match:
              all:
                - contextualAny: [receipt]
            actions:
              album: {{albumName}}
        """;

    private const string LiveArchiveYaml = """
        immich:
          url: "http://localhost:2283"
          apiKeyEnv: IMMICH_API_KEY
        scanner:
          batchSize: 10
          includeArchived: true
          includeExternalLibraries: true
        processing:
          dryRun: false
          maxAssetsPerRun: 20
          maxArchiveActionsPerRun: 1
          maxErrorsPerRun: 5
          ambiguousAssetPolicy: review
        state:
          path: ./state.db
        categories:
          - name: receipts
            enabled: true
            search:
              contextual: [receipt]
              ocr: [invoice]
            match:
              all:
                - contextualAny: [receipt]
                - ocrAny: [invoice]
            actions:
              archive: true
        """;

    private const string AmbiguousLiveYaml = """
        immich:
          url: "http://localhost:2283"
          apiKeyEnv: IMMICH_API_KEY
        scanner:
          batchSize: 10
        processing:
          dryRun: false
          maxAssetsPerRun: 20
          ambiguousAssetPolicy: review
        state:
          path: ./state.db
        categories:
          - name: receipts
            search:
              contextual: [receipt]
            match:
              all:
                - contextualAny: [receipt]
            actions:
              album: Receipts
          - name: invoices
            search:
              contextual: [invoice]
            match:
              all:
                - contextualAny: [invoice]
            actions:
              album: Invoices
        """;

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] expected = new[] { "/api/server/version", "/api/search/smart", "/api/search/metadata" };
}
