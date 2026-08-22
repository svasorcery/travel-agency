using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
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
            FlightsModule.ConfigureMarten(opts);
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
            string idempotencyKey,
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
            string idempotencyKey,
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
            NullFlightsMetricsImpl.Instance,
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
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<QuoteOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferExpired");
    }

    [Fact]
    public async Task Requote_with_aggregate_id_appends_OfferReQuoted()
    {
        var ct = TestContext.Current.CancellationToken;
        var initialOffer = BuildOffer();
        var provider = new SuccessProvider(initialOffer);
        var time = new FakeTimeProvider();

        // First quote — creates the stream.
        Guid streamId;
        await using (var session = _store.LightweightSession())
        {
            var first = await QuoteOfferHandler.Handle(
                new QuoteOfferCommand(initialOffer.ProviderOfferRef, ProviderId.Duffel),
                new IFlightBookingProvider[] { provider },
                session,
                NullFlightsMetricsImpl.Instance,
                time,
                NullLogger<QuoteOfferCommand>.Instance,
                ct
            );
            first.IsError.ShouldBeFalse();
            streamId = first.Value.AggregateId;
        }

        // Re-quote at a higher amount. The provider's refresh result reflects a new
        // total — the aggregate's TotalAmount must update to it via OfferReQuoted.
        var refreshedOffer = initialOffer with
        {
            TotalAmount = Money.Create(6200m, Rub).Value,
        };
        var refreshProvider = new SuccessProvider(refreshedOffer);

        await using (var session = _store.LightweightSession())
        {
            var second = await QuoteOfferHandler.Handle(
                new QuoteOfferCommand(refreshedOffer.ProviderOfferRef, ProviderId.Duffel, streamId),
                new IFlightBookingProvider[] { refreshProvider },
                session,
                NullFlightsMetricsImpl.Instance,
                time,
                NullLogger<QuoteOfferCommand>.Instance,
                ct
            );
            second.IsError.ShouldBeFalse();
            // No new stream created — the same aggregate id is returned.
            second.Value.AggregateId.ShouldBe(streamId);
        }

        // The stream now contains exactly one OfferReQuoted event and TotalAmount
        // reflects the new amount.
        await using var verifySession = _store.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);
        events
            .Count(e => e.Data is Travel.Modules.Flights.Core.DomainEvents.OfferReQuoted)
            .ShouldBe(1);

        var agg = await verifySession.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        agg.ShouldNotBeNull();
        agg.TotalAmount!.Amount.ShouldBe(6200m);
    }

    [Fact]
    public async Task Requote_on_non_quoted_stream_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var offer = BuildOffer();
        var provider = new SuccessProvider(offer);
        var time = new FakeTimeProvider();

        // Quote then hold the offer manually so the stream is no longer in
        // OfferQuoted state.
        Guid streamId;
        await using (var session = _store.LightweightSession())
        {
            var first = await QuoteOfferHandler.Handle(
                new QuoteOfferCommand(offer.ProviderOfferRef, ProviderId.Duffel),
                new IFlightBookingProvider[] { provider },
                session,
                NullFlightsMetricsImpl.Instance,
                time,
                NullLogger<QuoteOfferCommand>.Instance,
                ct
            );
            streamId = first.Value.AggregateId;
        }
        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(
                streamId,
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
                                .Modules.Flights.Core.ValueObjects.PhoneNumber.Create(
                                    "+79161234567"
                                )
                                .Value,
                            new DateOnly(2026, 5, 14)
                        )
                        .Value,
                    HeldUntil: DateTimeOffset.UtcNow.AddHours(2),
                    HeldAt: DateTimeOffset.UtcNow
                )
            );
            await session.SaveChangesAsync(ct);
        }

        await using (var session = _store.LightweightSession())
        {
            var result = await QuoteOfferHandler.Handle(
                new QuoteOfferCommand(offer.ProviderOfferRef, ProviderId.Duffel, streamId),
                new IFlightBookingProvider[] { provider },
                session,
                NullFlightsMetricsImpl.Instance,
                time,
                NullLogger<QuoteOfferCommand>.Instance,
                ct
            );
            result.IsError.ShouldBeTrue();
            result.FirstError.Code.ShouldBe("Flights.InvalidState");
        }
    }

    [Fact]
    public async Task Quote_with_empty_provider_offer_ref_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var offer = BuildOffer();
        var provider = new SuccessProvider(offer);
        var time = new FakeTimeProvider();

        await using var session = _store.LightweightSession();

        var result = await QuoteOfferHandler.Handle(
            new QuoteOfferCommand("", ProviderId.Duffel),
            new IFlightBookingProvider[] { provider },
            session,
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<QuoteOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.CommandInvalid");
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
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<QuoteOfferCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    // ─── Task 4.3 — price change flag on re-quote ─────────────────────────────

    [Fact]
    public async Task Requote_with_higher_price_reports_price_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var initialOffer = BuildOffer();
        var provider = new SuccessProvider(initialOffer);
        var time = new FakeTimeProvider();

        // First quote — creates the stream.
        Guid streamId;
        await using (var session = _store.LightweightSession())
        {
            var first = await QuoteOfferHandler.Handle(
                new QuoteOfferCommand(initialOffer.ProviderOfferRef, ProviderId.Duffel),
                new IFlightBookingProvider[] { provider },
                session,
                NullFlightsMetricsImpl.Instance,
                time,
                NullLogger<QuoteOfferCommand>.Instance,
                ct
            );
            first.IsError.ShouldBeFalse();
            streamId = first.Value.AggregateId;
        }

        // Re-quote at a higher amount.
        var higherAmount = Money.Create(9999m, Rub).Value;
        var refreshedOffer = initialOffer with { TotalAmount = higherAmount };
        var refreshProvider = new SuccessProvider(refreshedOffer);

        QuotedOfferResult second;
        await using (var session = _store.LightweightSession())
        {
            var secondResult = await QuoteOfferHandler.Handle(
                new QuoteOfferCommand(refreshedOffer.ProviderOfferRef, ProviderId.Duffel, streamId),
                new IFlightBookingProvider[] { refreshProvider },
                session,
                NullFlightsMetricsImpl.Instance,
                time,
                NullLogger<QuoteOfferCommand>.Instance,
                ct
            );
            secondResult.IsError.ShouldBeFalse();
            second = secondResult.Value;
        }

        // The result must flag the price change with the old and new amounts.
        second.PriceChanged.ShouldBeTrue();
        second.OldAmount.ShouldNotBeNull();
        second.OldAmount!.Amount.ShouldBe(initialOffer.TotalAmount.Amount);
        second.NewAmount.ShouldNotBeNull();
        second.NewAmount!.Amount.ShouldBe(9999m);

        // The offer returned reflects the new price.
        second.Offer.TotalAmount.Amount.ShouldBe(9999m);
    }
}
