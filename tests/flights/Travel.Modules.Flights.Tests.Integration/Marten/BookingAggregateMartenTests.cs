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
