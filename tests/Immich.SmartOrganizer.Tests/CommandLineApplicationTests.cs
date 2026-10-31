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

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] expected = new[] { "/api/server/version", "/api/search/smart", "/api/search/metadata" };
}
