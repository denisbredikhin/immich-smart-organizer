namespace Immich.SmartOrganizer;

internal interface ICommandEnvironment
{
    public string CurrentDirectory { get; }

    public string? GetEnvironmentVariable(string name);
}

internal sealed class SystemCommandEnvironment : ICommandEnvironment
{
    public string CurrentDirectory => Environment.CurrentDirectory;

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
