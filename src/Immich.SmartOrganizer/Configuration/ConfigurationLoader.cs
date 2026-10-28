using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Immich.SmartOrganizer.Configuration;

internal static class ConfigurationLoader
{
    private const string ConfigurationEnvironmentVariable = "IMMICH_SMART_ORGANIZER_CONFIG";
    internal const string DefaultConfigurationFileName = "immich-smart-organizer.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static async Task<LoadedConfiguration> LoadAsync(
        string? explicitPath,
        ICommandEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var configurationPath = ResolveConfigurationPath(explicitPath, environment);
        if (!File.Exists(configurationPath))
        {
            throw new ConfigurationException(
                $"Configuration file not found: '{configurationPath}'. Run 'immich-smart-organizer init' to create one.");
        }

        string yaml;
        try
        {
            yaml = await File.ReadAllTextAsync(configurationPath, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new ConfigurationException($"Could not read configuration file '{configurationPath}': {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ConfigurationException($"Could not read configuration file '{configurationPath}': {exception.Message}", exception);
        }

        AppConfiguration? configuration;
        try
        {
            using StringReader reader = new(yaml);
            configuration = Deserializer.Deserialize<AppConfiguration>(reader);
        }
        catch (YamlException exception)
        {
            throw new ConfigurationException(
                $"Invalid YAML or configuration property in '{configurationPath}': {exception.Message}",
                exception);
        }

        if (configuration is null)
        {
            throw new ConfigurationException($"Configuration file '{configurationPath}' is empty.");
        }

        var validation = ConfigurationValidator.Validate(configuration, configurationPath, environment);
        if (validation.Errors.Count > 0)
        {
            throw new ConfigurationException(string.Join(Environment.NewLine, validation.Errors));
        }

        return new LoadedConfiguration(configurationPath, configuration, validation.StateDatabasePath!);
    }

    private static string ResolveConfigurationPath(string? explicitPath, ICommandEnvironment environment)
    {
        if (explicitPath is not null && string.IsNullOrWhiteSpace(explicitPath))
        {
            throw new ConfigurationException("An explicit configuration path cannot be empty.");
        }

        var selectedPath = explicitPath;
        if (selectedPath is null)
        {
            selectedPath = environment.GetEnvironmentVariable(ConfigurationEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            selectedPath = DefaultConfigurationFileName;
        }

        try
        {
            return Path.GetFullPath(selectedPath, environment.CurrentDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigurationException($"The configuration path is invalid: {exception.Message}", exception);
        }
    }
}

internal sealed record LoadedConfiguration(
    string ConfigurationPath,
    AppConfiguration Configuration,
    string StateDatabasePath);
