using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Outbox;

/// <summary>
/// Boots a Wolverine host whose persistence + messaging wiring mirrors
/// <c>apps/Travel.Host/Program.cs</c>: Marten enrolled as a Wolverine transactional
/// outbox via <see cref="WolverineOptionsMartenExtensions.IntegrateWithWolverine"/>, the
/// EF <see cref="FlightsDbContext"/> enrolled via
/// <see cref="WolverineEntityCoreExtensions.UseEntityFrameworkCoreTransactions"/>, and the
/// <c>AutoApplyTransactions</c> / <c>UseDurableLocalQueues</c> policies enabled.
///
/// It deliberately omits the NATS transport (the outbox is exercised over local queues)
/// and runs the durability agent in solo mode so the persisted-message recovery loop is
/// active inside a single-node test host. Backed by a real Postgres Testcontainer so the
/// Marten event tables, the EF schema, and Wolverine's envelope tables all live in one
/// database — exactly the production topology.
/// </summary>
/// <remarks>
/// DRIFT HAZARD — keep this fixture in sync with the outbox wiring in
/// <c>apps/Travel.Host/Program.cs</c> (lines 48–58 Marten/IntegrateWithWolverine;
/// lines 83–84 AutoApplyTransactions/UseDurableLocalQueues; line 90
/// UseEntityFrameworkCoreTransactions). No automated test boots the real Program and
/// exercises the outbox path, so if that wiring changes the tests here will silently
/// continue testing the old configuration. Any change to the outbox lines in Program.cs
/// must be manually mirrored here.
/// </remarks>
public sealed class WolverineOutboxFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    public IHost Host { get; private set; } = default!;

    public OutboxProbeRecorder Probe { get; } = new();

    public string ConnectionString => _pg.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        var connectionString = _pg.GetConnectionString();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(Probe);
        builder.Services.AddSingleton(TimeProvider.System);

        // EF — FlightsDbContext. Program.cs registers this through Aspire's
        // AddNpgsqlDbContext<FlightsDbContext>; here we use the plain EF registration
        // (Aspire only layers OTel/health on top) so Wolverine's
        // UseEntityFrameworkCoreTransactions can augment the *existing* registration
        // without double-registering the context.
        builder.Services.AddDbContext<FlightsDbContext>(opts =>
        {
            opts.UseNpgsql(connectionString);
            opts.UseSnakeCaseNamingConvention();
        });

        // Marten — enrolled as a Wolverine transactional outbox, same as Program.cs.
        builder
            .Services.AddMarten(opts =>
            {
                opts.Connection(connectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(opts);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        builder.UseWolverine(opts =>
        {
            // Auto-transaction + durable local queue policies — the outbox guarantees.
            opts.Policies.AutoApplyTransactions();
            opts.Policies.UseDurableLocalQueues();

            // Make Wolverine aware of FlightsDbContext so a [Transactional] handler/endpoint
            // taking FlightsDbContext commits the DbContext save + the outbox message together.
            opts.UseEntityFrameworkCoreTransactions();

            // The probe handler lives in this test assembly.
            opts.Discovery.IncludeAssembly(typeof(WolverineOutboxFixture).Assembly);

            // Run the durability agent without leadership election (single-node test host).
            opts.Services.RunWolverineInSoloMode();
        });

        Host = builder.Build();
        await Host.StartAsync();

        // Apply the Flights EF migrations so the `flights` schema tables exist. Marten and
        // Wolverine create their own tables at host start, so the database is non-empty by
        // the time we get here — EnsureCreated would no-op. MigrateAsync creates the EF
        // tables regardless. (Wolverine's envelope tables come from the Marten integration.)
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }

        await _pg.DisposeAsync();
    }
}
