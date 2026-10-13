using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Travel.Modules.Support.Infrastructure.Persistence.Initialization;

namespace Travel.Modules.Support.Infrastructure.Retention;

public sealed class SupportRetentionWorker(
    IServiceScopeFactory scopes,
    SupportSchemaReadiness readiness,
    SupportRetentionStatus status,
    TimeProvider time,
    ILogger<SupportRetentionWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await readiness.WaitAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await scope
                    .ServiceProvider.GetRequiredService<SupportRetentionRunner>()
                    .RunPassAsync(stoppingToken);
                status.SetFailure(false);
                logger.LogInformation("Support retention pass deleted {Count} cases.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                status.SetFailure(true);
                // Provider exception details can include ciphertext/configuration; retain only a safe type.
                logger.LogError(
                    "Support retention pass failed; error type {ErrorType}.",
                    exception.GetType().Name
                );
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
