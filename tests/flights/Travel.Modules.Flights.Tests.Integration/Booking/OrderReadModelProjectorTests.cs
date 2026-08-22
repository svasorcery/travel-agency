using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

/// <summary>
/// Verifies that <see cref="OrderReadModelProjectorImpl"/> stamps the read model
/// with timestamps from the source events themselves — not the projection clock.
/// This makes the projection replay-stable: re-running the projector on the same
/// stream after time has advanced must produce the same TicketedAt/CancelledAt/
/// RefundedAt values.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OrderReadModelProjectorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private DocumentStore _store = default!;
    private FlightsDbContext _db = default!;

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(_pg.GetConnectionString());
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            FlightsModule.ConfigureMarten(opts);
        });

        var efOptions = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(efOptions);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        _store.Dispose();
        await _pg.DisposeAsync();
    }

    private static Itinerary BuildItinerary()
    {
        var seg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 40, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return Itinerary.Create(new[] { Slice.Create(new[] { seg }).Value }).Value;
    }

    private static PassengerInfo BuildPassenger() =>
        PassengerInfo
            .Create(
                "Ivan",
                "Petrov",
                new DateOnly(1990, 1, 1),
                Gender.Male,
                "ivan@example.com",
                PhoneNumber.Create("+79161234567").Value,
                new DateOnly(2026, 5, 14)
            )
            .Value;

    [Fact]
    public async Task Projection_uses_event_timestamps_and_is_replay_stable()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var quotedAt = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        var heldAt = new DateTimeOffset(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);
        var cancelledAt = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<BookingAggregate>(
                streamId,
                new OfferQuoted(
                    OfferId: OfferId.New(),
                    Itinerary: BuildItinerary(),
                    TotalAmount: Money.Create(5420m, Rub).Value,
                    ExpiresAt: quotedAt.AddMinutes(30),
                    ProviderRef: "off_test_" + Guid.NewGuid(),
                    QuotedAt: quotedAt
                ),
                new OfferHeld(
                    OrderId: "ord_" + Guid.NewGuid(),
                    Passenger: BuildPassenger(),
                    HeldUntil: heldAt.AddHours(2),
                    HeldAt: heldAt
                ),
                new OrderCancelled(CancelReason.User, cancelledAt)
            );
            await session.SaveChangesAsync(ct);
        }

        var projector = new OrderReadModelProjectorImpl(_db);

        // First projection.
        await using (var session = _store.LightweightSession())
        {
            var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(
                streamId,
                token: ct
            );
            await projector.Project(agg!, userId, ct);
        }

        var firstRow = await EntityFrameworkQueryableExtensions.FirstAsync(
            _db.Orders.AsNoTracking(),
            o => o.AggregateId == streamId,
            ct
        );
        firstRow.CancelledAt.ShouldBe(cancelledAt);
        firstRow.BookedAt.ShouldBe(heldAt);

        // Replay: project again. Timestamps must NOT change
        // — they come from the events, not the projection wall clock.
        await using (var session = _store.LightweightSession())
        {
            var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(
                streamId,
                token: ct
            );
            await projector.Project(agg!, userId, ct);
        }

        // Re-fetch — must be unchanged.
        _db.ChangeTracker.Clear();
        var secondRow = await EntityFrameworkQueryableExtensions.FirstAsync(
            _db.Orders.AsNoTracking(),
            o => o.AggregateId == streamId,
            ct
        );
        secondRow.CancelledAt.ShouldBe(
            cancelledAt,
            "CancelledAt must come from OrderCancelled.CancelledAt, not the projection clock"
        );
        secondRow.BookedAt.ShouldBe(
            heldAt,
            "BookedAt must come from OfferHeld.HeldAt, not the projection clock"
        );
    }
}
