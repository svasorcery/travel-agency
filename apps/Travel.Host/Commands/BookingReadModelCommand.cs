using JasperFx;
using Marten;
using Travel.Modules.Flights.Api.Composition;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Persistence;

namespace Travel.Host.Commands;

public static class BookingReadModelCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        IConfiguration? configuration = null,
        CancellationToken ct = default
    )
    {
        var request = BookingMaintenanceRequest.Parse(args);
        if (request is null)
        {
            await output.WriteLineAsync(
                "InvalidArguments: use validate; inspect --aggregate-id <guid>; rebuild --execute --exclusive-maintenance; replay --message-id <guid> --execute."
            );
            return 2;
        }
        try
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    Args = [],
                    EnvironmentName = configuration?["environment"],
                }
            );
            if (configuration is not null)
                builder.Configuration.AddConfiguration(configuration);
            builder.Logging.ClearProviders();
            var connection = builder.Configuration.GetConnectionString("travel");
            if (string.IsNullOrWhiteSpace(connection))
            {
                await output.WriteLineAsync(
                    "ConfigurationPrerequisite: ConnectionStrings:travel is required."
                );
                return 3;
            }
            builder.Services.AddBookingReadModelMaintenance(
                connection,
                request.Execute && request.ExclusiveMaintenance,
                output
            );
            builder
                .Services.AddMarten(options =>
                {
                    options.Connection(connection);
                    options.AutoCreateSchemaObjects = AutoCreate.None;
                    FlightsModule.ConfigureMarten(options);
                })
                .UseLightweightSessions()
                .IntegrateWithWolverine(integration => integration.AutoCreate = AutoCreate.None);
            builder.UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
                options.Durability.DurabilityAgentEnabled = false;
            });
            // Deliberately build but never start: no listeners, hosted services, consumers or initializers.
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            try
            {
                await BookingReadModelMaintenance.ValidateSchemaAsync(scope.ServiceProvider, ct);
                var stores = await host
                    .Services.GetRequiredService<MessageStoreCollection>()
                    .FindAllAsync<IDatabase>();
                if (stores.Count == 0)
                    throw new InvalidOperationException("MessageStoreMissing");
                foreach (var database in stores)
                    await database.AssertDatabaseMatchesConfigurationAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await output.WriteLineAsync(
                    "SchemaPrerequisite: compatible Flights, Marten and Wolverine schemas must already exist and be accessible; no schema changes were applied."
                );
                return 3;
            }
            return await BookingReadModelMaintenance.ExecuteAsync(
                scope.ServiceProvider,
                request,
                output,
                ct
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await output.WriteLineAsync(
                "Cancelled: completed streams remain committed; revalidate before resuming."
            );
            return 130;
        }
        catch (Exception)
        {
            await output.WriteLineAsync(
                "MaintenanceFailed: run may be partial; inspect and revalidate before resuming."
            );
            return 4;
        }
    }
}
