using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Infrastructure.Initialization;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Initialization;

public sealed class FlightsEfInitializer(FlightsDbContext dbContext, IHostEnvironment environment)
    : IInitializer
{
    public InitializationPhase Phase => InitializationPhase.RelationalSchema;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!environment.IsProduction())
        {
            await dbContext.Database.MigrateAsync(ct);
            return;
        }

        if ((await dbContext.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("Flights EF schema is not compatible.");
    }
}
