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
public sealed class QuoteOfferHandlerTests : IAsyncLifetime
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

    private static BookableOffer BuildOffer(DateTimeOffset? expiresAt = null) =>
        new(
            Id: OfferId.New(),
            Itinerary: BuildItinerary(),
            TotalAmount: Money.Create(5420m, Rub).Value,
            Provider: ProviderId.Duffel,
            FetchedAt: DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(20),
            FareConditions: new FareConditions(false, false, null, null),
            ProviderOfferRef: "off_test_" + Guid.NewGuid()
        );

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

    // ─── fake providers ─────────────────────────────────────────────────────────

    private sealed class SuccessProvider(BookableOffer offer) : IFlightBookingProvider
    {
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<BookableOffer>>(offer);

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer o,
            PassengerInfo p,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string orderId,
            PaymentRef payment,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string orderId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string orderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    private sealed class FailingRefreshProvider : IFlightBookingProvider
    {
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<BookableOffer>>(FlightsErrors.OfferExpired);

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer o,
            PassengerInfo p,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string orderId,
            PaymentRef payment,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string orderId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string orderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidOffer_StartsStream_AggregateIsOfferQuoted()
    {
        var ct = TestContext.Current.CancellationToken;
        var offer = BuildOffer();
        var provider = new SuccessProvider(offer);
        var time = new FakeTimeProvider();

        await using var session = _store.LightweightSession();

        var result = await QuoteOfferHandler.Handle(
            new QuoteOfferCommand(offer.ProviderOfferRef, ProviderId.Duffel),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetrics.Instance,
            time,
            NullLogger<QuoteOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.AggregateId.ShouldNotBe(Guid.Empty);
        result.Value.Offer.ShouldBe(offer);

        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(
            result.Value.AggregateId,
            token: ct
        );
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.OfferQuoted);
    }

    [Fact]
    public async Task ProviderRefreshFails_ReturnsError_NoStreamCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FailingRefreshProvider();
        var time = new FakeTimeProvider();

        await using var session = _store.LightweightSession();

        var result = await QuoteOfferHandler.Handle(
            new QuoteOfferCommand("off_expired", ProviderId.Duffel),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetrics.Instance,
            time,
            NullLogger<QuoteOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferExpired");
    }

    [Fact]
    public async Task UnknownProvider_ReturnsProviderUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new SuccessProvider(BuildOffer());
        var time = new FakeTimeProvider();
        var unknownProvider = new ProviderId("unknown-provider");

        await using var session = _store.LightweightSession();

        var result = await QuoteOfferHandler.Handle(
            new QuoteOfferCommand("off_xyz", unknownProvider),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetrics.Instance,
            time,
            NullLogger<QuoteOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
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
