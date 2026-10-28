using Immich.SmartOrganizer.Configuration;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ConfigurationValidatorTests
{
    [TestMethod]
    public async Task Validate_rejects_malformed_yaml()
    {
        using TemporaryDirectory directory = new();
        var configurationPath = await WriteConfigAsync(
            directory.Path,
            TestConfiguration.ValidYaml.Replace("batchSize: 100", "batchSize: [", StringComparison.Ordinal));

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "Invalid YAML");
    }

    [TestMethod]
    public async Task Validate_rejects_invalid_yaml_property_type()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "batchSize: 100",
            "batchSize: not-a-number",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));
        StringAssert.Contains(exception.Message, "Invalid YAML or configuration property");
        StringAssert.Contains(exception.Message, "Invalid YAML or configuration property");
    }

    [TestMethod]
    public async Task Validate_rejects_null_dry_run_setting()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace("dryRun: true", "dryRun: null", StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "processing.dryRun cannot be null.");
    }

    [TestMethod]
    public async Task Validate_rejects_duplicate_yaml_keys()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "dryRun: true",
            "dryRun: true\n  dryRun: false",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "duplicate key");
    }

    [TestMethod]
    public async Task Validate_rejects_unknown_property()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "  apiKeyEnv: IMMICH_API_KEY",
            "  apiKeyEnv: IMMICH_API_KEY\n  unsupportedOption: true",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "unsupportedOption");
    }

    [TestMethod]
    public async Task Validate_rejects_unsupported_action_property()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "archive: false",
            "archive: false\n      delete: true",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "delete");
    }

    [TestMethod]
    public async Task Validate_rejects_missing_immich_section()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var immichStart = source.IndexOf("immich:", StringComparison.Ordinal);
        var scannerStart = source.IndexOf("scanner:", immichStart, StringComparison.Ordinal);
        var yaml = source[..immichStart] + source[scannerStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "The 'immich' section is required.");
    }

    [TestMethod]
    public async Task Validate_rejects_missing_categories_section()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var categoriesStart = source.IndexOf("categories:", StringComparison.Ordinal);
        var loggingStart = source.IndexOf("logging:", categoriesStart, StringComparison.Ordinal);
        var yaml = source[..categoriesStart] + source[loggingStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "At least one category is required.");
    }

    [TestMethod]
    public async Task Validate_rejects_missing_category_sections()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var searchMarker = source.IndexOf("search:", StringComparison.Ordinal);
        var nextCategoryMarker = source.IndexOf("  - name: invoices", searchMarker, StringComparison.Ordinal);
        var sectionStart = source.LastIndexOf('\n', searchMarker) + 1;
        var nextCategoryStart = source.LastIndexOf('\n', nextCategoryMarker) + 1;
        var indentation = source[sectionStart..searchMarker];
        var yaml = source[..sectionStart]
            + $"{indentation}search: null\n{indentation}match: null\n{indentation}actions: null\n"
            + source[nextCategoryStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "categories[0].search is required.");
        StringAssert.Contains(exception.Message, "categories[0].match is required.");
        StringAssert.Contains(exception.Message, "categories[0].actions cannot be null.");
    }

    [TestMethod]
    public async Task Validate_rejects_search_without_queries()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var searchMarker = source.IndexOf("search:", StringComparison.Ordinal);
        var matchMarker = source.IndexOf("match:", searchMarker, StringComparison.Ordinal);
        var searchStart = source.LastIndexOf('\n', searchMarker) + 1;
        var matchStart = source.LastIndexOf('\n', matchMarker) + 1;
        var indentation = source[searchStart..searchMarker];
        var yaml = source[..searchStart]
            + $"{indentation}search:\n{indentation}  contextual: []\n{indentation}  ocr: []\n"
            + source[matchStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(
            exception.Message,
            "categories[0].search must contain at least one contextual or OCR query.");
    }

    [TestMethod]
    public async Task Validate_rejects_invalid_api_key_environment_variable_name()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "apiKeyEnv: IMMICH_API_KEY",
            "apiKeyEnv: invalid-name",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "immich.apiKeyEnv must be a valid environment variable name.");
    }

    [TestMethod]
    [DataRow("mode: incremental", "mode: unsupported", "scanner.mode")]
    [DataRow("ambiguousAssetPolicy: review", "ambiguousAssetPolicy: archive", "processing.ambiguousAssetPolicy")]
    public async Task Validate_rejects_unsupported_configuration_values(
        string originalValue,
        string replacement,
        string propertyName)
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(originalValue, replacement, StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, propertyName);
        StringAssert.Contains(exception.Message, "must be");
    }

    [TestMethod]
    public async Task Validate_rejects_invalid_url()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "http://localhost:2283",
            "ftp://immich.example",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "immich.url must be an absolute HTTP or HTTPS URL");
    }

    [TestMethod]
    [DataRow("batchSize: 100", "batchSize: 0", "scanner.batchSize", "1 and 500")]
    [DataRow("batchSize: 100", "batchSize: 501", "scanner.batchSize", "1 and 500")]
    [DataRow("concurrency: 2", "concurrency: 0", "scanner.concurrency", "1 and 8")]
    [DataRow("concurrency: 2", "concurrency: 9", "scanner.concurrency", "1 and 8")]
    [DataRow("lookbackDays: 7", "lookbackDays: 0", "scanner.lookbackDays", "1 and 3650")]
    [DataRow("lookbackDays: 7", "lookbackDays: 3651", "scanner.lookbackDays", "1 and 3650")]
    [DataRow("maxAssetsPerRun: 1000", "maxAssetsPerRun: 0", "processing.maxAssetsPerRun", "1 and 100000")]
    [DataRow("maxAssetsPerRun: 1000", "maxAssetsPerRun: 100001", "processing.maxAssetsPerRun", "1 and 100000")]
    [DataRow("maxArchiveActionsPerRun: 25", "maxArchiveActionsPerRun: -1", "processing.maxArchiveActionsPerRun", "0 and 1000")]
    [DataRow("maxArchiveActionsPerRun: 25", "maxArchiveActionsPerRun: 1001", "processing.maxArchiveActionsPerRun", "0 and 1000")]
    [DataRow("maxErrorsPerRun: 20", "maxErrorsPerRun: 0", "processing.maxErrorsPerRun", "1 and 1000")]
    [DataRow("maxErrorsPerRun: 20", "maxErrorsPerRun: 1001", "processing.maxErrorsPerRun", "1 and 1000")]
    public async Task Validate_rejects_out_of_range_numeric_limits(
        string originalValue,
        string replacement,
        string propertyName,
        string expectedRange)
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(originalValue, replacement, StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, $"{propertyName} must be between {expectedRange}");
    }

    [TestMethod]
    [DataRow("batchSize: 100", "batchSize: 1", "batchSize: 500", "scanner.batchSize", 1, 500)]
    [DataRow("concurrency: 2", "concurrency: 1", "concurrency: 8", "scanner.concurrency", 1, 8)]
    [DataRow("lookbackDays: 7", "lookbackDays: 1", "lookbackDays: 3650", "scanner.lookbackDays", 1, 3650)]
    [DataRow("maxAssetsPerRun: 1000", "maxAssetsPerRun: 1", "maxAssetsPerRun: 100000", "processing.maxAssetsPerRun", 1, 100000)]
    [DataRow("maxArchiveActionsPerRun: 25", "maxArchiveActionsPerRun: 0", "maxArchiveActionsPerRun: 1000", "processing.maxArchiveActionsPerRun", 0, 1000)]
    [DataRow("maxErrorsPerRun: 20", "maxErrorsPerRun: 1", "maxErrorsPerRun: 1000", "processing.maxErrorsPerRun", 1, 1000)]
    public async Task Validate_accepts_numeric_limit_boundaries(
        string originalValue,
        string minimumValue,
        string maximumValue,
        string propertyName,
        int expectedMinimum,
        int expectedMaximum)
    {
        using TemporaryDirectory directory = new();
        var baseYaml = TestConfiguration.ValidYaml.Replace(originalValue, minimumValue, StringComparison.Ordinal);
        var minimumPath = await WriteConfigAsync(directory.Path, baseYaml);
        var maximumYaml = TestConfiguration.ValidYaml.Replace(originalValue, maximumValue, StringComparison.Ordinal);
        var maximumPath = Path.Combine(directory.Path, "maximum.yaml");
        await File.WriteAllTextAsync(maximumPath, maximumYaml);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path);

        var minimum = await ConfigurationLoader.LoadAsync(minimumPath, environment);
        var maximum = await ConfigurationLoader.LoadAsync(maximumPath, environment);

        Assert.AreEqual(expectedMinimum, GetNumericValue(minimum.Configuration, propertyName));
        Assert.AreEqual(expectedMaximum, GetNumericValue(maximum.Configuration, propertyName));
    }

    [TestMethod]
    public async Task Validate_rejects_invalid_match_conditions()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var matchMarker = source.IndexOf("match:", StringComparison.Ordinal);
        var actionsMarker = source.IndexOf("actions:", matchMarker, StringComparison.Ordinal);
        Assert.IsTrue(matchMarker >= 0);
        Assert.IsTrue(actionsMarker > matchMarker);
        var matchStart = source.LastIndexOf('\n', matchMarker) + 1;
        var actionsStart = source.LastIndexOf('\n', actionsMarker) + 1;
        var indentation = source[matchStart..matchMarker];
        var yaml = source[..matchStart]
            + $"{indentation}match:\n{indentation}  all:\n{indentation}    - ocrAny: []\n"
            + source[actionsStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "match.all[0].ocrAny must contain at least one term");
    }

    [TestMethod]
    public async Task Validate_rejects_match_conditions_with_multiple_operators()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var matchMarker = source.IndexOf("match:", StringComparison.Ordinal);
        var actionsMarker = source.IndexOf("actions:", matchMarker, StringComparison.Ordinal);
        var matchStart = source.LastIndexOf('\n', matchMarker) + 1;
        var actionsStart = source.LastIndexOf('\n', actionsMarker) + 1;
        var indentation = source[matchStart..matchMarker];
        var yaml = source[..matchStart]
            + $"{indentation}match:\n{indentation}  all:\n{indentation}    - ocrAny: [receipt]\n"
            + $"{indentation}      contextualAny: [receipt]\n"
            + source[actionsStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "must specify exactly one supported condition operator");
    }

    [TestMethod]
    public async Task Validate_rejects_exclusions_without_a_positive_condition()
    {
        using TemporaryDirectory directory = new();
        var source = TestConfiguration.ValidYaml;
        var matchMarker = source.IndexOf("match:", StringComparison.Ordinal);
        var actionsMarker = source.IndexOf("actions:", matchMarker, StringComparison.Ordinal);
        var matchStart = source.LastIndexOf('\n', matchMarker) + 1;
        var actionsStart = source.LastIndexOf('\n', actionsMarker) + 1;
        var indentation = source[matchStart..matchMarker];
        var yaml = source[..matchStart]
            + $"{indentation}match:\n{indentation}  exclude:\n{indentation}    - ocrAny: [advertisement]\n"
            + source[actionsStart..];
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "match must contain at least one condition");
    }

    [TestMethod]
    public async Task Validate_rejects_duplicate_category_names()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace("name: invoices", "name: Receipts", StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "Category name 'Receipts' is duplicated.");
    }

    [TestMethod]
    public async Task Validate_reports_missing_api_key_environment_variable_without_secret_value()
    {
        using TemporaryDirectory directory = new();
        var configurationPath = await WriteConfigAsync(directory.Path, TestConfiguration.ValidYaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(
                configurationPath,
                CreateEnvironment(directory.Path, apiKey: null)));

        Assert.AreEqual(
            "Environment variable 'IMMICH_API_KEY' is not set or is empty.",
            exception.Message);
    }

    [TestMethod]
    public async Task Validate_rejects_credentials_embedded_in_immich_url()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "http://localhost:2283",
            "http://user:private-password@localhost:2283",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "without embedded credentials");
        Assert.IsFalse(exception.Message.Contains("private-password", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Validation_errors_never_include_api_key_value()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "http://localhost:2283",
            "ftp://immich.example",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path, apiKey: "private-api-key-value");

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, environment));

        StringAssert.Contains(exception.Message, "immich.url");
        Assert.IsFalse(exception.Message.Contains("private-api-key-value", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Validate_rejects_state_path_with_missing_parent_directory()
    {
        using TemporaryDirectory directory = new();
        var yaml = TestConfiguration.ValidYaml.Replace(
            "path: ./state.db",
            "path: ./missing/state.db",
            StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

        StringAssert.Contains(exception.Message, "directory for state.path does not exist");
    }

    [TestMethod]
    public async Task Validate_rejects_existing_state_file_in_non_writable_directory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix directory permissions are required for this test.");
            return;
        }

        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "state.db"), string.Empty);
        var yaml = TestConfiguration.ValidYaml.Replace("path: ./state.db", "path: state.db", StringComparison.Ordinal);
        var configurationPath = await WriteConfigAsync(directory.Path, yaml);
        var originalMode = File.GetUnixFileMode(directory.Path);

        try
        {
            File.SetUnixFileMode(
                directory.Path,
                originalMode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));

            var probePath = Path.Combine(directory.Path, ".write-probe");
            try
            {
                using FileStream probe = new(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                Assert.Inconclusive("The current user can write to a directory without write permissions.");
            }
            catch (Exception probeException) when (probeException is IOException or UnauthorizedAccessException)
            {
            }

            var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
                () => ConfigurationLoader.LoadAsync(configurationPath, CreateEnvironment(directory.Path)));

            StringAssert.Contains(exception.Message, "directory or file for state.path is not writable");
        }
        finally
        {
            File.SetUnixFileMode(directory.Path, originalMode);
        }
    }

    private static async Task<string> WriteConfigAsync(string directory, string yaml)
    {
        var path = Path.Combine(directory, "organizer.yaml");
        await File.WriteAllTextAsync(path, yaml);
        return path;
    }

    private static TestCommandEnvironment CreateEnvironment(
        string currentDirectory,
        string? apiKey = "test-api-key")
    {
        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal);
        if (apiKey is not null)
        {
            environmentVariables["IMMICH_API_KEY"] = apiKey;
        }

        return new TestCommandEnvironment(currentDirectory, environmentVariables);
    }

    private static int GetNumericValue(AppConfiguration configuration, string propertyName) =>
        propertyName switch
        {
            "scanner.batchSize" => configuration.Scanner!.BatchSize,
            "scanner.concurrency" => configuration.Scanner!.Concurrency,
            "scanner.lookbackDays" => configuration.Scanner!.LookbackDays,
            "processing.maxAssetsPerRun" => configuration.Processing!.MaxAssetsPerRun,
            "processing.maxArchiveActionsPerRun" => configuration.Processing!.MaxArchiveActionsPerRun,
            "processing.maxErrorsPerRun" => configuration.Processing!.MaxErrorsPerRun,
            _ => throw new AssertFailedException($"Unexpected configuration property '{propertyName}'.")
        };
}
