using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Infrastructure.Initialization;

namespace Travel.Modules.Support.Infrastructure.Persistence.Initialization;

public sealed class SupportEfInitializer(
    SupportDbContext db,
    IHostEnvironment environment,
    SupportSchemaReadiness readiness
) : IInitializer
{
    public InitializationPhase Phase => InitializationPhase.RelationalSchema;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!environment.IsProduction())
            await db.Database.MigrateAsync(ct);
        else if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Support EF schema is not compatible.");
        // Ensure the expected schema exists even when a migration assembly is accidentally empty.
        await db.Cases.AsNoTracking().Select(x => x.CaseId).Take(1).ToListAsync(ct);
        readiness.MarkReady();
    }
}
