using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Shared.Infrastructure.Initialization;

namespace Travel.Modules.Hotels.Infrastructure.Persistence;

public sealed class HotelsEfInitializer(
    HotelsDbContext db,
    IHostEnvironment environment,
    IOptions<HotelsOptions> options,
    HotelsSchemaReadiness readiness
) : IInitializer
{
    public InitializationPhase Phase => InitializationPhase.RelationalSchema;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled && !options.Value.ReceiverEnabled)
            return;
        if (!environment.IsProduction())
            await db.Database.MigrateAsync(ct);
        else if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Hotels schema is not compatible.");
        await db.Reservations.AsNoTracking().Select(r => r.Id).Take(1).ToArrayAsync(ct);
        readiness.MarkReady();
    }
}
