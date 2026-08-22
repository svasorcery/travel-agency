using System.Security.Cryptography;
using System.Text;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Travel.Host.Persistence.Initialization;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Initialization;
using Travel.Shared.Infrastructure.Initialization;
using Travel.Shared.TestInfrastructure;
using Wolverine;
using Wolverine.Marten;
using Xunit;

namespace Travel.Host.Tests.Integration.Initialization;

public sealed class DatabaseInitializationTests : IntegrationTestBase
{
    [Fact]
    public async Task Development_blank_database_is_initialized_in_phase_order_before_readiness()
    {
        await using var firstRun = BuildServices(Environments.Development);

        await firstRun
            .GetRequiredService<AppInitializer>()
            .StartAsync(TestContext.Current.CancellationToken);

        var appInitializer = firstRun.GetRequiredService<AppInitializer>();
        appInitializer.State.ShouldBe(InitializationState.Succeeded);
        appInitializer
            .Initializers.Select(status => (status.Name, status.Phase, status.State))
            .ShouldBe([
                (
                    typeof(WolverineMessageStoreInitializer).FullName!,
                    InitializationPhase.Platform,
                    InitializationState.Succeeded
                ),
                (
                    typeof(FlightsEfInitializer).FullName!,
                    InitializationPhase.RelationalSchema,
                    InitializationState.Succeeded
                ),
                (
                    typeof(FlightsMartenInitializer).FullName!,
                    InitializationPhase.EventStoreSchema,
                    InitializationState.Succeeded
                ),
            ]);
        (await ReadinessStatusAsync(firstRun)).ShouldBe(HealthStatus.Healthy);

        var firstSnapshot = await CaptureSnapshotAsync();
        firstSnapshot.Tables.ShouldContain("flights.__ef_migrations_history");
        firstSnapshot.Tables.ShouldContain("flights.deeplink_offers_cache");
        firstSnapshot.Tables.ShouldContain("flights.idempotency_keys");
        firstSnapshot.Tables.ShouldContain("flights.order_read_model");
        firstSnapshot.Tables.ShouldContain("flights.webhook_inbox");
        firstSnapshot.Tables.ShouldContain("public.mt_events");
        firstSnapshot.Tables.ShouldContain("public.mt_streams");
        firstSnapshot.Tables.ShouldContain("public.wolverine_incoming_envelopes");
        firstSnapshot.Tables.ShouldContain("public.wolverine_outgoing_envelopes");
        firstSnapshot.Migrations.ShouldBe(["20260513153403_FlightsM1Init"]);

        await using var secondRun = BuildServices(Environments.Development);
        await secondRun
            .GetRequiredService<AppInitializer>()
            .StartAsync(TestContext.Current.CancellationToken);

        AssertSnapshot(await CaptureSnapshotAsync(), firstSnapshot);
        (await ReadinessStatusAsync(secondRun)).ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Production_pending_wolverine_schema_fails_closed_without_mutating_database()
    {
        var before = await CaptureSnapshotAsync();
        await using var services = BuildServices(Environments.Production);

        await services
            .GetRequiredService<AppInitializer>()
            .StartAsync(TestContext.Current.CancellationToken);

        var appInitializer = services.GetRequiredService<AppInitializer>();
        appInitializer.State.ShouldBe(InitializationState.Failed);
        appInitializer.Initializers.Count.ShouldBe(3);
        appInitializer
            .Initializers[0]
            .Name.ShouldBe(typeof(WolverineMessageStoreInitializer).FullName);
        appInitializer.Initializers[0].State.ShouldBe(InitializationState.Failed);
        appInitializer.Initializers[1].State.ShouldBe(InitializationState.Pending);
        appInitializer.Initializers[2].State.ShouldBe(InitializationState.Pending);
        (await ReadinessStatusAsync(services)).ShouldBe(HealthStatus.Unhealthy);
        AssertSnapshot(await CaptureSnapshotAsync(), before);
    }

    [Fact]
    public async Task Production_pending_flights_migration_fails_closed_without_mutating_database()
    {
        await ApplyWolverineSchemaForTestSetupAsync();
        var before = await CaptureSnapshotAsync();
        await using var services = BuildServices(Environments.Production);

        await services
            .GetRequiredService<AppInitializer>()
            .StartAsync(TestContext.Current.CancellationToken);

        var appInitializer = services.GetRequiredService<AppInitializer>();
        appInitializer.State.ShouldBe(InitializationState.Failed);
        appInitializer.Initializers.Count.ShouldBe(3);
        appInitializer.Initializers[0].State.ShouldBe(InitializationState.Succeeded);
        appInitializer.Initializers[1].Name.ShouldBe(typeof(FlightsEfInitializer).FullName);
        appInitializer.Initializers[1].State.ShouldBe(InitializationState.Failed);
        appInitializer.Initializers[2].State.ShouldBe(InitializationState.Pending);
        (await ReadinessStatusAsync(services)).ShouldBe(HealthStatus.Unhealthy);
        AssertSnapshot(await CaptureSnapshotAsync(), before);
    }

    [Fact]
    public async Task Production_marten_mismatch_fails_closed_without_mutating_database()
    {
        await ApplyWolverineSchemaForTestSetupAsync();
        await ApplyFlightsEfMigrationsForTestSetupAsync();
        var before = await CaptureSnapshotAsync();
        await using var services = BuildServices(Environments.Production);

        await services
            .GetRequiredService<AppInitializer>()
            .StartAsync(TestContext.Current.CancellationToken);

        var appInitializer = services.GetRequiredService<AppInitializer>();
        appInitializer.State.ShouldBe(InitializationState.Failed);
        appInitializer.Initializers.Count.ShouldBe(3);
        appInitializer.Initializers[0].State.ShouldBe(InitializationState.Succeeded);
        appInitializer.Initializers[1].State.ShouldBe(InitializationState.Succeeded);
        appInitializer.Initializers[2].Name.ShouldBe(typeof(FlightsMartenInitializer).FullName);
        appInitializer.Initializers[2].State.ShouldBe(InitializationState.Failed);
        (await ReadinessStatusAsync(services)).ShouldBe(HealthStatus.Unhealthy);
        AssertSnapshot(await CaptureSnapshotAsync(), before);
    }

    [Fact]
    public async Task Production_compatible_schema_passes_without_mutating_database()
    {
        await ApplyWolverineSchemaForTestSetupAsync();
        await ApplyFlightsEfMigrationsForTestSetupAsync();
        await ApplyFlightsMartenSchemaForTestSetupAsync();
        var before = await CaptureSnapshotAsync();
        await using var services = BuildServices(Environments.Production);

        await services
            .GetRequiredService<AppInitializer>()
            .StartAsync(TestContext.Current.CancellationToken);

        services.GetRequiredService<AppInitializer>().State.ShouldBe(InitializationState.Succeeded);
        (await ReadinessStatusAsync(services)).ShouldBe(HealthStatus.Healthy);
        AssertSnapshot(await CaptureSnapshotAsync(), before);
    }

    private ServiceProvider BuildServices(string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:travel"] = ConnectionString,
                    ["ConnectionStrings:redis"] = "localhost:6379",
                }
            )
            .Build();
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = environmentName,
                ApplicationName = "Travel.Host.Initialization.Tests",
            }
        );
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddLogging();
        builder.AddFlightsModule();
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(ConnectionString);
                options.AutoCreateSchemaObjects = AutoCreate.None;
                FlightsModule.ConfigureMarten(options);
            })
            .IntegrateWithWolverine(integration => integration.AutoCreate = AutoCreate.None);
        builder.Services.AddWolverine(options =>
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None
        );
        builder.Services.AddAppInitialization();
        builder.Services.AddInitializer<WolverineMessageStoreInitializer>();
        return builder.Services.BuildServiceProvider();
    }

    private async Task ApplyWolverineSchemaForTestSetupAsync()
    {
        await using var services = BuildServices(Environments.Development);
        var initializer = services
            .GetServices<IInitializer>()
            .OfType<WolverineMessageStoreInitializer>()
            .ShouldHaveSingleItem();
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
    }

    private async Task ApplyFlightsEfMigrationsForTestSetupAsync()
    {
        await using var services = BuildServices(Environments.Development);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private async Task ApplyFlightsMartenSchemaForTestSetupAsync()
    {
        using var store = DocumentStore.For(options =>
        {
            options.Connection(ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.None;
            FlightsModule.ConfigureMarten(options);
        });
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All);
    }

    private async Task<DatabaseSnapshot> CaptureSnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var schemas = await QueryStringsAsync(
            connection,
            """
            SELECT nspname
            FROM pg_namespace
            WHERE nspname IN ('flights', 'public')
            ORDER BY nspname;
            """
        );
        var tables = await QueryStringsAsync(
            connection,
            """
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE table_schema IN ('flights', 'public')
              AND table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name;
            """
        );
        var migrations = tables.Contains("flights.__ef_migrations_history")
            ? await QueryStringsAsync(
                connection,
                "SELECT migration_id FROM flights.__ef_migrations_history ORDER BY migration_id;"
            )
            : [];
        return new DatabaseSnapshot(
            schemas,
            tables,
            migrations,
            await CaptureDatabaseFingerprintAsync(connection)
        );
    }

    private static async Task<string> CaptureDatabaseFingerprintAsync(NpgsqlConnection connection)
    {
        var entries = await QueryStringsAsync(
            connection,
            """
            WITH user_namespaces AS (
                SELECT oid, nspname
                FROM pg_namespace
                WHERE nspname <> 'information_schema'
                  AND nspname NOT LIKE 'pg_%'
            )
            SELECT entry
            FROM (
                SELECT 'schema|' || n.nspname AS entry
                FROM user_namespaces n

                UNION ALL

                SELECT 'relation|' || n.nspname || '|' || c.relname || '|'
                       || c.relkind::text || '|' || c.relpersistence::text
                FROM pg_class c
                JOIN user_namespaces n ON n.oid = c.relnamespace
                WHERE c.relkind IN ('r', 'p', 'S', 'v', 'm', 'f')

                UNION ALL

                SELECT 'column|' || n.nspname || '|' || c.relname || '|'
                       || a.attnum::text || '|' || a.attname || '|'
                       || pg_catalog.format_type(a.atttypid, a.atttypmod) || '|'
                       || a.attnotnull::text || '|' || a.attidentity::text || '|'
                       || a.attgenerated::text || '|'
                       || COALESCE(coll.collname, '') || '|'
                       || COALESCE(pg_get_expr(ad.adbin, ad.adrelid), '')
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN user_namespaces n ON n.oid = c.relnamespace
                LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                LEFT JOIN pg_collation coll ON coll.oid = a.attcollation
                WHERE a.attnum > 0
                  AND NOT a.attisdropped
                  AND c.relkind IN ('r', 'p', 'v', 'm', 'f')

                UNION ALL

                SELECT 'index|' || schemaname || '|' || indexname || '|' || indexdef
                FROM pg_indexes
                WHERE schemaname <> 'information_schema'
                  AND schemaname NOT LIKE 'pg_%'

                UNION ALL

                SELECT 'constraint|' || n.nspname || '|' || c.relname || '|'
                       || con.conname || '|' || con.contype::text || '|'
                       || pg_get_constraintdef(con.oid, true)
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN user_namespaces n ON n.oid = c.relnamespace

                UNION ALL

                SELECT 'trigger|' || n.nspname || '|' || c.relname || '|'
                       || t.tgname || '|' || pg_get_triggerdef(t.oid, true)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN user_namespaces n ON n.oid = c.relnamespace
                WHERE NOT t.tgisinternal

                UNION ALL

                SELECT 'sequence|' || schemaname || '|' || sequencename || '|'
                       || data_type || '|' || start_value::text || '|' || min_value::text || '|'
                       || max_value::text || '|' || increment_by::text || '|' || cycle::text || '|'
                       || cache_size::text || '|' || COALESCE(last_value::text, '')
                FROM pg_sequences
                WHERE schemaname <> 'information_schema'
                  AND schemaname NOT LIKE 'pg_%'

                UNION ALL

                SELECT 'function|' || n.nspname || '|' || p.proname || '|'
                       || pg_get_function_identity_arguments(p.oid) || '|'
                       || pg_get_functiondef(p.oid)
                FROM pg_proc p
                JOIN user_namespaces n ON n.oid = p.pronamespace
                WHERE p.prokind IN ('f', 'p')

                UNION ALL

                SELECT 'type|' || n.nspname || '|' || t.typname || '|'
                       || t.typtype::text || '|' || COALESCE(pg_get_expr(t.typdefaultbin, 0), '')
                FROM pg_type t
                JOIN user_namespaces n ON n.oid = t.typnamespace
                WHERE t.typtype IN ('d', 'e')
            ) catalog
            ORDER BY entry;
            """
        );

        var tables = await QueryStringsAsync(
            connection,
            """
            SELECT schemaname || '.' || tablename
            FROM pg_tables
            WHERE schemaname <> 'information_schema'
              AND schemaname NOT LIKE 'pg_%'
            ORDER BY schemaname, tablename;
            """
        );
        var commandBuilder = new NpgsqlCommandBuilder();
        foreach (var table in tables)
        {
            var separator = table.IndexOf('.');
            var schema = commandBuilder.QuoteIdentifier(table[..separator]);
            var name = commandBuilder.QuoteIdentifier(table[(separator + 1)..]);
            var rows = await QueryStringsAsync(
                connection,
                $"""
                SELECT COALESCE(string_agg(row_json, E'\n' ORDER BY row_json), '<empty>')
                FROM (SELECT to_jsonb(data)::text AS row_json FROM {schema}.{name} data) rows;
                """
            );
            entries = [.. entries, $"data|{table}|{rows.ShouldHaveSingleItem()}"];
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', entries));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static async Task<string[]> QueryStringsAsync(
        NpgsqlConnection connection,
        string commandText
    )
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken
        );
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            values.Add(reader.GetString(0));
        return [.. values];
    }

    private static async Task<HealthStatus> ReadinessStatusAsync(ServiceProvider services)
    {
        var result = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Name == InitializationHealthCheck.Name,
                TestContext.Current.CancellationToken
            );
        return result.Status;
    }

    private static void AssertSnapshot(DatabaseSnapshot actual, DatabaseSnapshot expected)
    {
        actual.Schemas.ShouldBe(expected.Schemas);
        actual.Tables.ShouldBe(expected.Tables);
        actual.Migrations.ShouldBe(expected.Migrations);
        actual.Fingerprint.ShouldBe(expected.Fingerprint);
    }

    private sealed record DatabaseSnapshot(
        string[] Schemas,
        string[] Tables,
        string[] Migrations,
        string Fingerprint
    );
}
