using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Travel.Shared.Infrastructure.Initialization;

/// Sequential by design: initializers may have implicit ordering through DI scope
/// (e.g. Marten schema apply before any module that reads from it). Independence
/// is a guideline, not a guarantee — modules should not rely on cross-initializer
/// state but the runtime does not enforce parallelism.
internal sealed class AppInitializer(IServiceProvider services, ILogger<AppInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var initializers = scope.ServiceProvider.GetServices<IInitializer>().ToArray();

        logger.LogInformation("Running {Count} initializer(s)", initializers.Length);

        foreach (var initializer in initializers)
        {
            var name = initializer.GetType().Name;
            logger.LogInformation("Initializing: {Name}", name);
            await initializer.InitializeAsync(ct);
            logger.LogInformation("Done: {Name}", name);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
