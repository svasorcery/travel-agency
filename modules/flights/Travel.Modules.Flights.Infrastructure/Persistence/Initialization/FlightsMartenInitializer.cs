using JasperFx;
using Marten;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Infrastructure.Initialization;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Initialization;

public sealed class FlightsMartenInitializer(
    IDocumentStore documentStore,
    IHostEnvironment environment
) : IInitializer
{
    public InitializationPhase Phase => InitializationPhase.EventStoreSchema;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (environment.IsProduction())
        {
            await documentStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync(ct);
            return;
        }

        ct.ThrowIfCancellationRequested();
        await documentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All);
    }
}
