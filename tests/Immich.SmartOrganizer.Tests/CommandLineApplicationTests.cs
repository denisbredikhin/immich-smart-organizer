using System.Net;
using Immich.SmartOrganizer.Cli;

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
        StringAssert.Contains(result.Output, "immich-smart-organizer config validate [--config PATH]");
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public async Task No_arguments_print_usage_and_return_success()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path);

        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, "Usage:");
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
        StringAssert.Contains(result.Error, "unsupported argument '--unknown'");
        StringAssert.Contains(result.Error, "Use --help");
    }

    [TestMethod]
    public async Task Init_writes_valid_default_configuration()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["init"]);
        var configurationPath = Path.Combine(directory.Path, "immich-smart-organizer.yaml");

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(File.Exists(configurationPath));
        var yaml = await File.ReadAllTextAsync(configurationPath);
        StringAssert.Contains(yaml, "dryRun: true");
        StringAssert.Contains(yaml, "archive: false");
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
        StringAssert.Contains(validation.Output, "Configuration is valid.");
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
        StringAssert.Contains(result.Output, Path.Combine(outputDirectory, "custom.yaml"));
    }

    [TestMethod]
    public async Task Init_rejects_output_path_with_missing_parent_directory()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["init", "--output", "missing/custom.yaml"]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "output directory does not exist");
        Assert.IsFalse(Directory.Exists(Path.Combine(directory.Path, "missing")));
    }

    [TestMethod]
    public async Task Init_rejects_unsupported_options()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["init", "--unknown"]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "usage: init [--output PATH]");
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Init_refuses_to_overwrite_existing_file()
    {
        using TemporaryDirectory directory = new();
        const string existingContent = "preserve this file";
        var configurationPath = Path.Combine(directory.Path, "immich-smart-organizer.yaml");
        await File.WriteAllTextAsync(configurationPath, existingContent);

        var result = await ExecuteAsync(directory.Path, ["init"]);

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(existingContent, await File.ReadAllTextAsync(configurationPath));
        StringAssert.Contains(result.Error, "refusing to overwrite");
    }

    [TestMethod]
    public async Task Config_validate_uses_explicit_config_path()
    {
        using TemporaryDirectory directory = new();
        var configDirectory = Path.Combine(directory.Path, "configs");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "explicit.yaml"),
            TestConfiguration.ValidYaml);
        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };

        var result = await ExecuteAsync(
            directory.Path,
            ["config", "validate", "--config", Path.Combine("configs", "explicit.yaml")],
            environmentVariables);

        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.Output, Path.Combine(configDirectory, "explicit.yaml"));
        Assert.IsFalse(result.Output.Contains("test-api-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Config_validate_suggests_init_when_default_config_is_missing()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["config", "validate"]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "Run 'immich-smart-organizer init' to create one.");
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Config_validate_rejects_missing_config_path()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["config", "validate", "--config"]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "usage: config validate [--config PATH]");
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
        StringAssert.Contains(result.Error, "usage: config validate [--config PATH]");
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
        StringAssert.Contains(result.Error, "between 1 and 100000");
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
        StringAssert.Contains(result.Error, $"option '{option}' {expectedMessage}");
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
        StringAssert.Contains(result.Error, "must be an integer between 1 and 100000");
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Run_rejects_duplicate_scan_options()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", "--full", "--full"]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "cannot be specified more than once");
    }

    [TestMethod]
    public async Task Run_rejects_unknown_scan_option()
    {
        using TemporaryDirectory directory = new();
        var result = await ExecuteAsync(directory.Path, ["scan", "--unknown"]);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "unsupported scan option '--unknown'");
    }

    [TestMethod]
    public async Task Scan_checks_version_then_discovers_deduplicated_candidates_without_mutations()
    {
        using TemporaryDirectory directory = new();
        var configurationPath = Path.Combine(directory.Path, "scan.yaml");
        await File.WriteAllTextAsync(configurationPath, TestConfiguration.ScanYaml);
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
            () => new HttpClient(handler));

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Error);
        StringAssert.Contains(result.Output, "Immich version: 3.3.5");
        StringAssert.Contains(result.Output, "Candidates discovered: 3");
        StringAssert.Contains(result.Output, "Coverage: search windows completed");
        StringAssert.Contains(result.Output, "No library changes were made.");
        CollectionAssert.AreEqual(
            new[] { "/api/server/version", "/api/search/smart", "/api/search/metadata" },
            handler.Requests.Select(static request => request.Uri.AbsolutePath).ToArray());
        Assert.IsFalse(handler.Requests[0].Headers.ContainsKey("x-api-key"));
        Assert.IsTrue(handler.Requests.Skip(1).All(static request =>
            request.Headers.TryGetValue("x-api-key", out var values)
            && values.SequenceEqual(["test-api-key"])));
        StringAssert.Contains(handler.Requests[1].Body!, "\"query\":\"receipt\"");
        StringAssert.Contains(handler.Requests[1].Body!, "\"filter\":{\"type\":{\"eq\":\"IMAGE\"}}");
        StringAssert.Contains(handler.Requests[2].Body!, "\"matches\":\"invoice\"");
        StringAssert.Contains(handler.Requests[2].Body!, "\"type\":{\"eq\":\"IMAGE\"}");
        Assert.IsTrue(handler.Requests.All(static request =>
            request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Post
                && request.Uri.AbsolutePath.StartsWith("/api/search/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Scan_rejects_immich_url_with_fragment_as_configuration_error()
    {
        using TemporaryDirectory directory = new();
        var scanYaml = TestConfiguration.ScanYaml.Replace(
            "http://localhost:2283",
            "http://localhost:2283/#/photos",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), scanYaml);
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Invalid configuration must not send requests."));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            () => new HttpClient(handler));

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "Configuration error:");
        StringAssert.Contains(result.Error, "without embedded credentials, query, or fragment");
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Scan_rejects_unsupported_server_before_searching()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), TestConfiguration.ScanYaml);
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json("""{"major":3,"minor":1,"patch":9,"prerelease":0}""")));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            () => new HttpClient(handler));

        Assert.AreEqual(4, result.ExitCode);
        StringAssert.Contains(result.Error, "is not supported");
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual("/api/server/version", handler.Requests[0].Uri.AbsolutePath);
    }

    [TestMethod]
    public async Task Scan_maps_authentication_failure_to_exit_code_three()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), TestConfiguration.ScanYaml);
        StubHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.Uri.AbsolutePath == "/api/server/version"
                ? StubHttpMessageHandler.Json("""{"major":3,"minor":3,"patch":5,"prerelease":0}""")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml"],
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["IMMICH_API_KEY"] = "test-api-key" },
            () => new HttpClient(handler));

        Assert.AreEqual(3, result.ExitCode);
        StringAssert.Contains(result.Error, "authentication or permission failure");
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual(string.Empty, result.Output);
    }

    [TestMethod]
    public async Task Scan_returns_partial_exit_code_when_a_query_exhausts_retries()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "scan.yaml"), TestConfiguration.ScanYaml);
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
            () => new HttpClient(handler));

        Assert.AreEqual(5, result.ExitCode);
        StringAssert.Contains(result.Output, "Candidates discovered: 1");
        StringAssert.Contains(result.Output, "Coverage: partial");
        StringAssert.Contains(result.Output, "failed after bounded retries");
        Assert.AreEqual(5, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Run_rejects_unconfigured_category()
    {
        using TemporaryDirectory directory = new();
        var configurationPath = Path.Combine(directory.Path, "scan.yaml");
        await File.WriteAllTextAsync(configurationPath, TestConfiguration.ValidYaml);
        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };

        var result = await ExecuteAsync(
            directory.Path,
            ["scan", "--config", "scan.yaml", "--category", "missing"],
            environmentVariables);

        Assert.AreEqual(2, result.ExitCode);
        StringAssert.Contains(result.Error, "category 'missing' is not configured");
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
        Func<HttpClient>? httpClientFactory = null)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        TestCommandEnvironment environment = new(currentDirectory, environmentVariables);

        var exitCode = await CommandLineApplication.RunAsync(
            args,
            output,
            error,
            environment,
            httpClientFactory: httpClientFactory);
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

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
