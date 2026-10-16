using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Travel.Shared.Infrastructure.Initialization;

/// <summary>Starts an owner-selected hosted service only after all schema gates succeed.</summary>
public sealed class InitializationGatedHostedService(
    AppInitializer initializer,
    IServiceProvider services,
    Func<IServiceProvider, object> factory
) : IHostedService
{
    private IHostedService? started;

    public async Task StartAsync(CancellationToken ct)
    {
        await initializer.StartAsync(ct);
        if (initializer.State != InitializationState.Succeeded)
            return;

        var service = (IHostedService)factory(services);
        await service.StartAsync(ct);
        started = service;
    }

    public Task StopAsync(CancellationToken ct) => started?.StopAsync(ct) ?? Task.CompletedTask;
}

public static class InitializationHostedServiceExtensions
{
    /// <summary>
    /// The process selects its messaging owner explicitly. Shared knows nothing about
    /// transports. Preserve registration order and singleton ownership of resolved services.
    /// </summary>
    public static IServiceCollection GateHostedServiceFactoriesAfterInitialization(
        this IServiceCollection services,
        Assembly owner
    )
    {
        for (var index = 0; index < services.Count; index++)
        {
            var descriptor = services[index];
            if (
                descriptor.ServiceType != typeof(IHostedService)
                || descriptor.ImplementationFactory is not { } factory
                || factory.Method.Module.Assembly != owner
            )
                continue;

            if (descriptor.Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException(
                    "Initialization gating requires a singleton hosted service."
                );
            Func<IServiceProvider, InitializationGatedHostedService> gatedFactory = provider =>
                new(provider.GetRequiredService<AppInitializer>(), provider, factory);
            services[index] = ServiceDescriptor.Singleton<IHostedService>(gatedFactory);
        }
        return services;
    }
}
