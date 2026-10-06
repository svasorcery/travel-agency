using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

// CI-only: initialization starts disposable Postgres and applies test-store schema.
// Compile locally; never execute this fixture outside the existing CI.
public sealed class CancellationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    public IHost Host { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(postgres.GetConnectionString());
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(options =>
        {
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(CancellationRecoveryStorageProbeHandler));
            options.Discovery.IncludeType(typeof(CancellationAdmissionProbeHandler));
            options.Discovery.IncludeType(typeof(CancellationStorageProbeHandler));
            options.Discovery.IncludeType(typeof(CancellationReconcileProbeHandler));
            options.Services.RunWolverineInSoloMode();
        });
        Host = builder.Build();
        await Host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }
        await postgres.DisposeAsync();
    }

    internal static IDomainEvent[] HeldEvents(Guid owner, DateTimeOffset now)
    {
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return
        [
            new OfferQuoted(
                OfferId.New(),
                Itinerary.Create([Slice.Create([segment]).Value]).Value,
                Money.Create(100m, CurrencyCode.Create("USD").Value).Value,
                now.AddHours(1),
                "off_fictional",
                now
            ),
            new OfferHeldV3(
                "ord_fictional",
                ProtectedPassengerPartySnapshot.Create(1, "fictional-protected").Value,
                now.AddHours(1),
                now,
                owner,
                Guid.NewGuid(),
                1
            ),
            new BookingMutationCoordinationEnabled(now),
        ];
    }
}

public sealed record CancellationStorageProbe(Guid AggregateId, Guid WorkId);

public static class CancellationStorageProbeHandler
{
    [WolverineHandler]
    public static void Handle(CancellationStorageProbe message) => _ = message;
}

public static class CancellationReconcileProbeHandler
{
    [WolverineHandler]
    public static void Handle(ReconcileOrderReadModel message) => _ = message;
}

// This fixture tests admission persistence only; real consumers/restart are tested separately.
public static class CancellationAdmissionProbeHandler
{
    [WolverineHandler]
    public static void Handle(ExecuteCancellationPreparation message) => _ = message;
}

// Persistence fixture consumers are inert; actual recovery/host restart is a separate CI suite.
public static class CancellationRecoveryStorageProbeHandler
{
    [WolverineHandler]
    public static void Handle(ObserveCancellation message) => _ = message;

    [WolverineHandler]
    public static void Handle(AdmissionDeadline message) => _ = message;

    [WolverineHandler]
    public static void Handle(RecoveryDeadline message) => _ = message;

    [WolverineHandler]
    public static void Handle(ConfirmationBarrierDeadline message) => _ = message;
}
