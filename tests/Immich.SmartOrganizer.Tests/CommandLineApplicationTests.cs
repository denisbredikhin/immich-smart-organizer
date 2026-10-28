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
    public async Task Run_reports_scan_not_available_without_claiming_success()
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
            ["scan", "--config", "scan.yaml", "--dry-run", "--full", "--limit", "10", "--category", "receipts", "--verbose"],
            environmentVariables);

        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Error, "Scan is not available in this stage.");
        StringAssert.Contains(result.Error, "No Immich requests were sent.");
        Assert.AreEqual(string.Empty, result.Output);
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
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        TestCommandEnvironment environment = new(currentDirectory, environmentVariables);

        var exitCode = await CommandLineApplication.RunAsync(args, output, error, environment);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
