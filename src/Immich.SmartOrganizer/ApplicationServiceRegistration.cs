using Immich.SmartOrganizer.Cli;
using Immich.SmartOrganizer.Immich;
using Microsoft.Extensions.DependencyInjection;

namespace Immich.SmartOrganizer;

internal static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ICommandEnvironment, SystemCommandEnvironment>();
        services.AddTransient<CommandLineApplication>();
        services.AddImmichHttpClient();
        return services;
    }
}
