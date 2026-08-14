using JasperFx;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Infrastructure.Initialization;
using Weasel.Core.Migrations;
using Wolverine.Persistence;

namespace Travel.Host.Persistence.Initialization;

public sealed class WolverineMessageStoreInitializer(
    MessageStoreCollection messageStores,
    IHostEnvironment environment
) : IInitializer
{
    public InitializationPhase Phase => InitializationPhase.Platform;

    public async Task InitializeAsync(CancellationToken ct)
    {
        var databases = await messageStores.FindAllAsync<IDatabase>();
        if (databases.Count == 0)
            throw new InvalidOperationException("Wolverine message storage is not configured.");

        foreach (var database in databases.OrderBy(database => database.Id.ToString()))
        {
            if (environment.IsProduction())
            {
                await database.AssertDatabaseMatchesConfigurationAsync(ct);
                continue;
            }

            await database.ApplyAllConfiguredChangesToDatabaseAsync(
                AutoCreate.All,
                new ReconnectionOptions(),
                ct
            );
        }
    }
}
