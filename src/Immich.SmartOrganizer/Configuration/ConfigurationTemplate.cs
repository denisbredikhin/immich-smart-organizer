using System.Reflection;
using System.Text;

namespace Immich.SmartOrganizer.Configuration;

internal static class ConfigurationTemplate
{
    private const string ResourceName = "Immich.SmartOrganizer.default-config.yaml";

    public static async Task<string> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The default configuration template is missing.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
