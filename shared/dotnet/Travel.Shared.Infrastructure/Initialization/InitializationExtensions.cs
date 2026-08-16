using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Travel.Shared.Infrastructure.Initialization;

public static class InitializationExtensions
{
    /// <summary>
    /// Adds deterministic application initialization and its readiness signal. Call once in Program.cs.
    /// </summary>
    public static IServiceCollection AddAppInitialization(this IServiceCollection services)
    {
        services.AddSingleton<AppInitializer>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<AppInitializer>()
        );
        services
            .AddHealthChecks()
            .AddCheck<InitializationHealthCheck>(
                InitializationHealthCheck.Name,
                tags: [InitializationHealthCheck.ReadinessTag]
            );
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IInitializer"/> implementation.
    /// </summary>
    public static IServiceCollection AddInitializer<T>(this IServiceCollection services)
        where T : class, IInitializer
    {
        services.AddScoped<IInitializer, T>();
        return services;
    }
}
