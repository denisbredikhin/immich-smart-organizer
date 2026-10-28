using Immich.SmartOrganizer.Cli;

namespace Immich.SmartOrganizer.Tests;

internal sealed class TestCommandEnvironment : ICommandEnvironment
{
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;

    public TestCommandEnvironment(
        string currentDirectory,
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        CurrentDirectory = currentDirectory;
        _environmentVariables = environmentVariables;
    }

    public string CurrentDirectory { get; }

    public string? GetEnvironmentVariable(string name) =>
        _environmentVariables.TryGetValue(name, out var value) ? value : null;
}
