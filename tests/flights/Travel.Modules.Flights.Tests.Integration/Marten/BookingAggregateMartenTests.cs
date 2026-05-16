using JasperFx;
using Marten;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Marten;

[Trait("Category", "Integration")]
public sealed class BookingAggregateMartenTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private DocumentStore _store = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(_pg.GetConnectionString());
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.ConfigureFlightsBooking();
        });
    }

    public async ValueTask DisposeAsync()
    {
        _store.Dispose();
        await _pg.DisposeAsync();
    }

    private static OfferQuoted SampleOfferQuoted()
    {
        var iata = (string c) => IataCode.Create(c).Value;
        var rub = CurrencyCode.Create("RUB").Value;
        var money = Money.Create(5420m, rub).Value;
        var seg = Segment
            .Create(
                iata("LED"),
                iata("DME"),
                new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 40, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var slice = Slice.Create(new[] { seg }).Value;
        var itinerary = Itinerary.Create(new[] { slice }).Value;
        return new OfferQuoted(
            OfferId.New(),
            itinerary,
            money,
            DateTimeOffset.UtcNow.AddMinutes(20),
            "off_123",
            DateTimeOffset.UtcNow
        );
    }

    [Fact]
    public async Task Aggregate_version_and_id_track_stream_version_after_rebuild()
    {
        var streamId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        var ct = TestContext.Current.CancellationToken;

        // Append N=3 events: OfferQuoted, OfferHeld, OrderCancelled.
        session.Events.StartStream<BookingAggregate>(
            streamId,
            SampleOfferQuoted(),
            new Travel.Modules.Flights.Core.DomainEvents.OfferHeld(
                OrderId: "ord_" + Guid.NewGuid(),
                Passenger: Travel
                    .Modules.Flights.Core.ValueObjects.PassengerInfo.Create(
                        "Ivan",
                        "Petrov",
                        new DateOnly(1990, 1, 1),
                        Travel.Modules.Flights.Core.ValueObjects.Gender.Male,
                        "ivan@example.com",
                        Travel
                            .Modules.Flights.Core.ValueObjects.PhoneNumber.Create("+79161234567")
                            .Value,
                        new DateOnly(2026, 5, 14)
                    )
                    .Value,
                HeldUntil: DateTimeOffset.UtcNow.AddHours(2),
                HeldAt: DateTimeOffset.UtcNow
            ),
            new Travel.Modules.Flights.Core.DomainEvents.OrderCancelled(
                Travel.Modules.Flights.Core.DomainEvents.CancelReason.User,
                DateTimeOffset.UtcNow
            )
        );
        await session.SaveChangesAsync(ct);

        var aggregate = await session.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        aggregate.ShouldNotBeNull();
        aggregate.Id.ShouldBe(streamId);
        aggregate.Version.ShouldBe(3);
    }

    [Fact]
    public async Task Can_append_OfferQuoted_and_rebuild_aggregate()
    {
        var streamId = Guid.NewGuid();
        await using var session = _store.LightweightSession();

        var iata = (string c) => IataCode.Create(c).Value;
        var rub = CurrencyCode.Create("RUB").Value;
        var money = Money.Create(5420m, rub).Value;
        var seg = Segment
            .Create(
                iata("LED"),
                iata("DME"),
                new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 40, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var slice = Slice.Create(new[] { seg }).Value;
        var itinerary = Itinerary.Create(new[] { slice }).Value;
        var quoted = new OfferQuoted(
            OfferId.New(),
            itinerary,
            money,
            DateTimeOffset.UtcNow.AddMinutes(20),
            "off_123",
            DateTimeOffset.UtcNow
        );

        var ct = TestContext.Current.CancellationToken;
        session.Events.StartStream<BookingAggregate>(streamId, quoted);
        await session.SaveChangesAsync(ct);

        var aggregate = await session.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(BookingStatus.OfferQuoted);
    }
}
