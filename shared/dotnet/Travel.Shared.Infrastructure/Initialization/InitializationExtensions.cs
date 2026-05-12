using Microsoft.Extensions.DependencyInjection;

namespace Travel.Shared.Infrastructure.Initialization;

public static class InitializationExtensions
{
    /// Adds the AppInitializer hosted service. Call once in Program.cs.
    public static IServiceCollection AddAppInitialization(this IServiceCollection services)
    {
        services.AddHostedService<AppInitializer>();
        return services;
    }

    /// Registers an IInitializer implementation. Each module calls this in its
    /// module-extension method (e.g. AddFlightsModule registers FlightsInitializer).
    public static IServiceCollection AddInitializer<T>(this IServiceCollection services)
        where T : class, IInitializer
    {
        services.AddScoped<IInitializer, T>();
        return services;
    }
}
