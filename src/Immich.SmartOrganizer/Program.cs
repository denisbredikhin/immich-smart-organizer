using System.Reflection;
using Immich.SmartOrganizer.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace Immich.SmartOrganizer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellationSource = new();
        void cancelHandler(object? _, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        }
        Console.CancelKeyPress += cancelHandler;

        try
        {
            ServiceCollection services = new();
            services.AddApplicationServices();
            using var serviceProvider = services.BuildServiceProvider();
            var application = serviceProvider.GetRequiredService<CommandLineApplication>();
            return await application.RunAsync(
                args,
                Console.Out,
                Console.Error,
                cancellationToken: cancellationSource.Token);
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
