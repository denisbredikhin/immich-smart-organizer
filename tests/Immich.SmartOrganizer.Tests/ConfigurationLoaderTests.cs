using Immich.SmartOrganizer.Configuration;

namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class ConfigurationLoaderTests
{
    [TestMethod]
    public async Task Load_uses_explicit_path_before_environment_and_current_directory()
    {
        using TemporaryDirectory directory = new();
        var currentDirectoryPath = await WriteConfigAsync(directory.Path, "current.yaml", withBatchSize: 11);
        var environmentPath = await WriteConfigAsync(directory.Path, "environment.yaml", withBatchSize: 22);
        var explicitPath = await WriteConfigAsync(directory.Path, "explicit.yaml", withBatchSize: 33);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path, environmentPath);

        var loaded = await ConfigurationLoader.LoadAsync(explicitPath, environment);

        Assert.AreEqual(Path.GetFullPath(explicitPath), loaded.ConfigurationPath);
        Assert.AreEqual(33, loaded.Configuration.Scanner!.BatchSize);
        Assert.AreEqual(Path.Combine(directory.Path, "state.db"), loaded.StateDatabasePath);
        Assert.IsTrue(File.Exists(currentDirectoryPath));
    }

    [TestMethod]
    public async Task Load_uses_environment_path_when_explicit_path_is_missing()
    {
        using TemporaryDirectory directory = new();
        var environmentPath = await WriteConfigAsync(directory.Path, "environment.yaml", withBatchSize: 22);
        await WriteConfigAsync(directory.Path, "immich-smart-organizer.yaml", withBatchSize: 11);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path, environmentPath);

        var loaded = await ConfigurationLoader.LoadAsync(null, environment);

        Assert.AreEqual(Path.GetFullPath(environmentPath), loaded.ConfigurationPath);
        Assert.AreEqual(22, loaded.Configuration.Scanner!.BatchSize);
    }

    [TestMethod]
    public async Task Load_uses_current_directory_config_when_no_higher_precedence_path_exists()
    {
        using TemporaryDirectory directory = new();
        await WriteConfigAsync(directory.Path, "immich-smart-organizer.yaml", withBatchSize: 11);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path);

        var loaded = await ConfigurationLoader.LoadAsync(null, environment);

        Assert.AreEqual(
            Path.Combine(directory.Path, "immich-smart-organizer.yaml"),
            loaded.ConfigurationPath);
        Assert.AreEqual(11, loaded.Configuration.Scanner!.BatchSize);
    }

    [TestMethod]
    public async Task Load_resolves_relative_config_path_from_current_directory()
    {
        using TemporaryDirectory directory = new();
        var configDirectory = Path.Combine(directory.Path, "configs");
        Directory.CreateDirectory(configDirectory);
        await WriteConfigAsync(configDirectory, "organizer.yaml", withBatchSize: 41);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path);

        var loaded = await ConfigurationLoader.LoadAsync(
            Path.Combine("configs", "organizer.yaml"),
            environment);

        Assert.AreEqual(
            Path.Combine(configDirectory, "organizer.yaml"),
            loaded.ConfigurationPath);
        Assert.AreEqual(41, loaded.Configuration.Scanner!.BatchSize);
    }

    [TestMethod]
    public async Task Load_rejects_empty_explicit_path()
    {
        using TemporaryDirectory directory = new();
        TestCommandEnvironment environment = CreateEnvironment(directory.Path);

        var exception = await Assert.ThrowsExactlyAsync<ConfigurationException>(
            () => ConfigurationLoader.LoadAsync(string.Empty, environment));

        StringAssert.Contains(exception.Message, "explicit configuration path cannot be empty");
    }

    [TestMethod]
    public async Task Load_resolves_relative_state_path_from_configuration_directory()
    {
        using TemporaryDirectory directory = new();
        var configDirectory = Path.Combine(directory.Path, "configs");
        var stateDirectory = Path.Combine(directory.Path, "state");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(stateDirectory);
        var yaml = TestConfiguration.ValidYaml.Replace(
            "path: ./state.db",
            "path: ../state/state.db",
            StringComparison.Ordinal);
        var configurationPath = Path.Combine(configDirectory, "organizer.yaml");
        await File.WriteAllTextAsync(configurationPath, yaml);
        TestCommandEnvironment environment = CreateEnvironment(directory.Path);

        var loaded = await ConfigurationLoader.LoadAsync(configurationPath, environment);

        Assert.AreEqual(Path.Combine(stateDirectory, "state.db"), loaded.StateDatabasePath);
    }

    private static async Task<string> WriteConfigAsync(
        string directory,
        string fileName,
        int withBatchSize)
    {
        var path = Path.Combine(directory, fileName);
        var yaml = TestConfiguration.ValidYaml.Replace(
            "batchSize: 100",
            $"batchSize: {withBatchSize}",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, yaml);
        return path;
    }

    private static TestCommandEnvironment CreateEnvironment(
        string currentDirectory,
        string? configPath = null)
    {
        Dictionary<string, string?> environmentVariables = new(StringComparer.Ordinal)
        {
            ["IMMICH_API_KEY"] = "test-api-key"
        };
        if (configPath is not null)
        {
            environmentVariables["IMMICH_SMART_ORGANIZER_CONFIG"] = configPath;
        }

        return new TestCommandEnvironment(currentDirectory, environmentVariables);
    }
}
