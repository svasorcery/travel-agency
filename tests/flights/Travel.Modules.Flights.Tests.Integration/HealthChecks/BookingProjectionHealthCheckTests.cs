using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.HealthChecks;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Wolverine;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.HealthChecks;

[Trait("Category", "Integration")]
public sealed class BookingProjectionHealthCheckTests : IClassFixture<BookingDeliveryFixture>
{
    private readonly BookingDeliveryFixture _fixture;

    public BookingProjectionHealthCheckTests(BookingDeliveryFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Empty_delivery_storage_is_available_but_does_not_prove_freshness()
    {
        var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        var result = await new BookingProjectionHealthCheck(runtime).CheckHealthAsync(new(), Ct);
        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldNotBeNull().ShouldContain("No dead letters");
    }

    [Fact]
    public async Task Sentinel_row_closes_bootstrap_readiness()
    {
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var id = Guid.NewGuid();
        db.Orders.Add(
            new OrderReadModelEntity
            {
                Id = Guid.NewGuid(),
                AggregateId = id,
                ProjectedStreamVersion = -1,
                Status = "Held",
                Currency = "USD",
                ItineraryJson = "{}",
                PassengerInfoJson = "{}",
                BookedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync(Ct);
        try
        {
            var result = await new BookingProjectionBootstrapHealthCheck(db).CheckHealthAsync(
                new(),
                Ct
            );
            result.Status.ShouldBe(HealthStatus.Unhealthy);
        }
        finally
        {
            await db.Orders.Where(x => x.AggregateId == id).ExecuteDeleteAsync(Ct);
        }
    }

    [Fact]
    public async Task Bootstrap_storage_failure_is_unhealthy()
    {
        var options = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1"
            )
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new FlightsDbContext(options);
        var result = await new BookingProjectionBootstrapHealthCheck(db).CheckHealthAsync(
            new(),
            Ct
        );
        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Delivery_diagnostic_storage_failure_is_unhealthy()
    {
        await using var fixture = new BookingDeliveryFixture();
        await fixture.InitializeAsync();
        var runtime = fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        await fixture.StopPostgresAsync();
        var result = await new BookingProjectionHealthCheck(runtime).CheckHealthAsync(new(), Ct);
        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Persisted_dead_letter_degrades_diagnostic_health()
    {
        await using var fixture = new BookingDeliveryFixture();
        await fixture.InitializeAsync();
        var id = Guid.NewGuid();
        var scenario = fixture.Probe.Add(id, failures: int.MaxValue, terminal: true);
        await fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new ReconcileOrderReadModel(id));
        var runtime = fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (scenario.Attempts.IsEmpty)
            await Task.Delay(50, timeout.Token);
        var envelopeId = scenario.Attempts.First().EnvelopeId;
        while (await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(envelopeId) is null)
            await Task.Delay(50, timeout.Token);

        var result = await new BookingProjectionHealthCheck(runtime).CheckHealthAsync(new(), Ct);
        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNull().ShouldContain("dead letters");
    }

    [Fact]
    public async Task No_dead_letter_or_sentinel_does_not_hide_a_missing_order_row()
    {
        var id = await SeedHeldOrderAsync();
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<
            DbContextOptions<FlightsDbContext>
        >();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var reconciler = new OrderReadModelReconciler(
            store,
            options,
            new BookingProjectionMaintenanceContext()
        );
        var validation = await reconciler.ValidateAsync(id, Ct);
        validation.Issues.ShouldContain(x => x.Code == "ProjectionMissing");
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        (
            await new BookingProjectionBootstrapHealthCheck(db).CheckHealthAsync(new(), Ct)
        ).Status.ShouldBe(HealthStatus.Healthy);
        (
            await new BookingProjectionHealthCheck(
                scope.ServiceProvider.GetRequiredService<IWolverineRuntime>()
            ).CheckHealthAsync(new(), Ct)
        ).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Ordinary_lag_keeps_bootstrap_readiness_open()
    {
        var id = await SeedHeldOrderAsync();
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<
            DbContextOptions<FlightsDbContext>
        >();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var reconciler = new OrderReadModelReconciler(
            store,
            options,
            new BookingProjectionMaintenanceContext()
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        await using (var session = store.LightweightSession())
        {
            session.Events.Append(
                id,
                new PaymentAuthorized(
                    PaymentRef.New(),
                    BookingReconcilerFixture.Amount,
                    BookingReconcilerFixture.Now
                )
            );
            await session.SaveChangesAsync(Ct);
        }
        var validation = await reconciler.ValidateAsync(id, Ct);
        validation.Issues.ShouldContain(x => x.Code == "CheckpointBehind");
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        (
            await new BookingProjectionBootstrapHealthCheck(db).CheckHealthAsync(new(), Ct)
        ).Status.ShouldBe(HealthStatus.Healthy);
    }

    private async Task<Guid> SeedHeldOrderAsync()
    {
        var id = Guid.NewGuid();
        var now = BookingReconcilerFixture.Now;
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
        var quote = new OfferQuoted(
            OfferId.New(),
            Itinerary.Create([Slice.Create([segment]).Value]).Value,
            BookingReconcilerFixture.Amount,
            now.AddHours(1),
            "off-test",
            now
        );
        await using var session = _fixture
            .Host.Services.GetRequiredService<IDocumentStore>()
            .LightweightSession();
        session.Events.StartStream<BookingAggregate>(id, quote);
        session.Events.Append(id, BookingReconcilerFixture.Held(Guid.NewGuid()));
        await session.SaveChangesAsync(Ct);
        return id;
    }
}
