using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Infrastructure.Persistence;

public sealed class HotelRetentionWorker(
    IServiceScopeFactory scopes,
    HotelsSchemaReadiness readiness,
    IOptions<HotelsOptions> options,
    TimeProvider time,
    ILogger<HotelRetentionWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled && !options.Value.ReceiverEnabled)
            return;
        await readiness.WaitAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5), time);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope
                    .ServiceProvider.GetRequiredService<HotelReservationStore>()
                    .PurgePrivateContentAsync(time.GetUtcNow(), stoppingToken);
                var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
                var until = time.GetUtcNow().AddDays(-90);
                var ids = await db
                    .Inbox.AsNoTracking()
                    .Where(i =>
                        i.ReceivedAt < until && i.ProcessedAt != null && i.ProtectedPayload != null
                    )
                    .OrderBy(i => i.ReceivedAt)
                    .Take(100)
                    .Select(i => i.Id)
                    .ToArrayAsync(stoppingToken);
                await db
                    .Inbox.Where(i => ids.Contains(i.Id) && i.ProcessedAt != null)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(i => i.ProtectedPayload, (string?)null),
                        stoppingToken
                    );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError("Hotels private-content cleanup failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
