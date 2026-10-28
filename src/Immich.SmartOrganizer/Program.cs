using System.Reflection;
using Immich.SmartOrganizer.Cli;

namespace Immich.SmartOrganizer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellationSource = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            return await CommandLineApplication.RunAsync(
                args,
                Console.Out,
                Console.Error,
                new SystemCommandEnvironment(),
                cancellationSource.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}

internal static class ApplicationVersion
{
    public static string Current
    {
        get
        {
            var informationalVersion = typeof(ApplicationVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informationalVersion))
            {
                throw new InvalidOperationException("The assembly informational version is missing.");
            }

            return informationalVersion.Split('+', 2)[0];
        }
    }
}
