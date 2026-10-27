namespace Immich.SmartOrganizer.Tests;

[TestClass]
public sealed class CommandLineApplicationTests
{
    [TestMethod]
    [DataRow("--help")]
    [DataRow("-h")]
    public void Help_options_print_usage_and_return_success(string argument)
    {
        var result = Execute(argument);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            $"Immich Smart Organizer{Environment.NewLine}Usage: immich-smart-organizer [--help | --version]{Environment.NewLine}",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public void No_arguments_print_usage_and_return_success()
    {
        var result = Execute();

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            $"Immich Smart Organizer{Environment.NewLine}Usage: immich-smart-organizer [--help | --version]{Environment.NewLine}",
            result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    [DataRow("--version")]
    [DataRow("-v")]
    public void Version_options_print_package_version_and_return_success(string argument)
    {
        var result = Execute(argument);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("0.1.0-alpha.1" + Environment.NewLine, result.Output);
        Assert.AreEqual(string.Empty, result.Error);
    }

    [TestMethod]
    public void Unknown_argument_prints_diagnostic_and_returns_invalid_arguments()
    {
        var result = Execute("--unknown");

        Assert.AreEqual(2, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Output);
        Assert.AreEqual(
            $"Error: unsupported argument '--unknown'.{Environment.NewLine}Use --help to see available options.{Environment.NewLine}",
            result.Error);
    }

    private static (int ExitCode, string Output, string Error) Execute(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }
}
