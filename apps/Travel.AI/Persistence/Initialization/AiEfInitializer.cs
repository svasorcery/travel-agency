using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Infrastructure.Initialization;

namespace Travel.AI.Persistence.Initialization;

public sealed class AiEfInitializer(AiDbContext dbContext, IHostEnvironment environment)
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
            throw new InvalidOperationException("Travel.AI EF schema is not compatible.");
    }
}
