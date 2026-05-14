using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class HoldOfferHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    private DocumentStore _store = default!;

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
            opts.ConfigureFlightsBooking();
        });
    }

    public async ValueTask DisposeAsync()
    {
        _store.Dispose();
        await _pg.DisposeAsync();
    }

    // ─── helpers ───────────────────────────────────────────────────────────────

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
        var slice = Slice.Create(new[] { seg }).Value;
        return Itinerary.Create(new[] { slice }).Value;
    }

    private static PassengerInfo BuildPassenger() =>
        PassengerInfo
            .Create(
                "Ivan",
                "Petrov",
                new DateOnly(1990, 1, 1),
                Gender.Male,
                "ivan@example.com",
                PhoneNumber.Create("+79161234567").Value
            )
            .Value;

    private async Task<Guid> SeedOfferQuotedStream(DateTimeOffset expiresAt)
    {
        var streamId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        var ct = TestContext.Current.CancellationToken;

        session.Events.StartStream<BookingAggregate>(
            streamId,
            new OfferQuoted(
                OfferId: OfferId.New(),
                Itinerary: BuildItinerary(),
                TotalAmount: Money.Create(5420m, Rub).Value,
                ExpiresAt: expiresAt,
                ProviderRef: "off_test_" + Guid.NewGuid(),
                QuotedAt: DateTimeOffset.UtcNow
            )
        );
        await session.SaveChangesAsync(ct);
        return streamId;
    }

    // ─── fake providers ─────────────────────────────────────────────────────────

    private sealed class SuccessHoldProvider(string orderId, DateTimeOffset heldUntil)
        : IFlightBookingProvider
    {
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            PassengerInfo passenger,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<HeldOrder>>(new HeldOrder(orderId, heldUntil));

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidOffer_AppendsOfferHeld_AggregateIsHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var streamId = await SeedOfferQuotedStream(now.AddMinutes(20));

        var expectedOrderId = "ord_" + Guid.NewGuid();
        var heldUntil = now.AddHours(2);
        var provider = new SuccessHoldProvider(expectedOrderId, heldUntil);
        var time = new FakeTimeProvider(now);

        await using var session = _store.LightweightSession();

        var result = await HoldOfferHandler.Handle(
            new HoldOfferCommand(streamId, BuildPassenger()),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetrics.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.AggregateId.ShouldBe(streamId);
        result.Value.ProviderOrderId.ShouldBe(expectedOrderId);
        result.Value.HeldUntil.ShouldBe(heldUntil);

        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Held);
        agg.ProviderOrderId.ShouldBe(expectedOrderId);
    }

    [Fact]
    public async Task ExpiredOffer_ReturnsOfferExpired_StreamUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        // Seed with an already-expired ExpiresAt
        var streamId = await SeedOfferQuotedStream(now.AddMinutes(-5));

        var provider = new SuccessHoldProvider("wont_be_used", now.AddHours(2));
        var time = new FakeTimeProvider(now);

        await using var session = _store.LightweightSession();

        var result = await HoldOfferHandler.Handle(
            new HoldOfferCommand(streamId, BuildPassenger()),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetrics.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferExpired");

        // Aggregate should still be OfferQuoted (no OfferHeld appended)
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.OfferQuoted);
    }

    [Fact]
    public async Task NonExistentAggregate_ReturnsOfferNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var nonExistentId = Guid.NewGuid();
        var provider = new SuccessHoldProvider("wont_be_used", DateTimeOffset.UtcNow.AddHours(2));
        var time = new FakeTimeProvider();

        await using var session = _store.LightweightSession();

        var result = await HoldOfferHandler.Handle(
            new HoldOfferCommand(nonExistentId, BuildPassenger()),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetrics.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferNotFound");
    }
}

file sealed class NullFlightsMetrics : IFlightsMetrics
{
    public static readonly NullFlightsMetrics Instance = new();

    public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

    public void RecordSearchError(string provider) { }

    public void RecordPaymentOutcome(bool success) { }

    public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

    public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

    public void RecordWebhookReceived(string eventType) { }

    public void RecordWebhookProcessingLag(double ms, string eventType) { }
}
