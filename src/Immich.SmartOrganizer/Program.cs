using System.Reflection;

namespace Immich.SmartOrganizer;

internal static class Program
{
    public static int Main(string[] args)
    {
        return CommandLineApplication.Run(args, Console.Out, Console.Error);
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

internal static class CommandLineApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is [] or ["--help"] or ["-h"])
        {
            output.WriteLine("Immich Smart Organizer");
            output.WriteLine("Usage: immich-smart-organizer [--help | --version]");
            return 0;
        }

        if (args is ["--version"] or ["-v"])
        {
            output.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        error.WriteLine(args.Length > 1
            ? "Error: expected a single option (--help, -h, --version, or -v)."
            : $"Error: unsupported argument '{args[0]}'.");
        error.WriteLine("Use --help to see available options.");
        return 2;
    }
}
