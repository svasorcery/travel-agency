using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
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
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class CancelOrderHandlerTests : IAsyncLifetime
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
            opts.ConfigureFlightsBooking();
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
                PhoneNumber.Create("+79161234567").Value,
                new DateOnly(2026, 5, 14)
            )
            .Value;

    private static Money BuildMoney() => Money.Create(5420m, Rub).Value;

    /// <summary>Seeds OfferQuoted only (no provider order).</summary>
    private async Task<Guid> SeedQuotedStream()
    {
        var streamId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        await using var session = _store.LightweightSession();

        session.Events.StartStream<BookingAggregate>(
            streamId,
            new OfferQuoted(
                OfferId: OfferId.New(),
                Itinerary: BuildItinerary(),
                TotalAmount: BuildMoney(),
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
                ProviderRef: "off_test_" + Guid.NewGuid(),
                QuotedAt: DateTimeOffset.UtcNow
            )
        );
        await session.SaveChangesAsync(ct);
        return streamId;
    }

    /// <summary>Seeds OfferQuoted + OfferHeld + OrderConfirmed (has provider order).</summary>
    private async Task<(Guid StreamId, string ProviderOrderId)> SeedConfirmedStream()
    {
        var streamId = Guid.NewGuid();
        var providerOrderId = "ord_" + Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        await using var session = _store.LightweightSession();
        var paymentRef = PaymentRef.New();

        session.Events.StartStream<BookingAggregate>(
            streamId,
            new OfferQuoted(
                OfferId: OfferId.New(),
                Itinerary: BuildItinerary(),
                TotalAmount: BuildMoney(),
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
                ProviderRef: "off_test_" + Guid.NewGuid(),
                QuotedAt: DateTimeOffset.UtcNow
            )
        );
        await session.SaveChangesAsync(ct);

        session.Events.Append(
            streamId,
            new OfferHeld(
                OrderId: providerOrderId,
                Passenger: BuildPassenger(),
                HeldUntil: DateTimeOffset.UtcNow.AddHours(2),
                HeldAt: DateTimeOffset.UtcNow
            ),
            new PaymentAuthorized(paymentRef, BuildMoney(), DateTimeOffset.UtcNow),
            new OrderConfirmed(providerOrderId, paymentRef, DateTimeOffset.UtcNow)
        );
        await session.SaveChangesAsync(ct);
        return (streamId, providerOrderId);
    }

    /// <summary>Seeds a stream already in Cancelled state.</summary>
    private async Task<(Guid StreamId, int EventCount)> SeedCancelledStream()
    {
        var streamId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        await using var session = _store.LightweightSession();

        session.Events.StartStream<BookingAggregate>(
            streamId,
            new OfferQuoted(
                OfferId: OfferId.New(),
                Itinerary: BuildItinerary(),
                TotalAmount: BuildMoney(),
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
                ProviderRef: "off_test_" + Guid.NewGuid(),
                QuotedAt: DateTimeOffset.UtcNow
            ),
            new OrderCancelled(CancelReason.User, DateTimeOffset.UtcNow)
        );
        await session.SaveChangesAsync(ct);

        // Return initial event count for idempotency assertion
        var state = await session.Events.FetchStreamStateAsync(streamId, ct);
        return (streamId, (int)state!.Version);
    }

    // ─── fake collaborators ─────────────────────────────────────────────────────

    private sealed class RecordingBookingProvider : IFlightBookingProvider
    {
        public bool CancelOrderCalled { get; private set; }
        public string? CancelledOrderId { get; private set; }

        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            PassengerInfo passenger,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct)
        {
            CancelOrderCalled = true;
            CancelledOrderId = providerOrderId;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    private sealed class NoOpBookingProvider : IFlightBookingProvider
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
        ) => throw new NotImplementedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) =>
            throw new NotSupportedException(
                "CancelOrderAsync should not be called for streams without a provider order"
            );

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    // RecordingMessageBus / NullFlightsMetrics live in SharedFakes.cs.
    private static RecordingMartenOutbox NewRecordingBus() => new();

    private OrderReadModelProjectorImpl CreateProjector() => new OrderReadModelProjectorImpl(_db);

    private static readonly IFlightsMetrics NullMetrics = NullFlightsMetricsImpl.Instance;

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelFromConfirmed_ProviderCalled_StreamCancelled_ReadModelUpdated_NotificationPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, expectedProviderOrderId) = await SeedConfirmedStream();
        var userId = Guid.NewGuid();

        var provider = new RecordingBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe("Cancelled");
        result.Value.AggregateId.ShouldBe(streamId);

        // Provider was called
        provider.CancelOrderCalled.ShouldBeTrue();
        provider.CancelledOrderId.ShouldBe(expectedProviderOrderId);

        // Stream in Cancelled state
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Cancelled);

        // Read model updated
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Cancelled");

        // Notification published
        bus.Published.OfType<OrderCancelledNotification>()
            .ShouldHaveSingleItem()
            .AggregateId.ShouldBe(streamId);
    }

    [Fact]
    public async Task CancelFromQuoted_ProviderNotCalled_StreamCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedQuotedStream();
        var userId = Guid.NewGuid();

        // NoOpBookingProvider will throw if CancelOrderAsync is called
        var provider = new NoOpBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe("Cancelled");

        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_on_ticketed_order_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = Guid.NewGuid();
        var paymentRef = PaymentRef.New();
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<BookingAggregate>(
                streamId,
                new OfferQuoted(
                    OfferId.New(),
                    BuildItinerary(),
                    BuildMoney(),
                    DateTimeOffset.UtcNow.AddMinutes(30),
                    "off_test_" + Guid.NewGuid(),
                    DateTimeOffset.UtcNow
                ),
                new OfferHeld(
                    "ord_" + Guid.NewGuid(),
                    BuildPassenger(),
                    DateTimeOffset.UtcNow.AddHours(2),
                    DateTimeOffset.UtcNow
                ),
                new PaymentAuthorized(paymentRef, BuildMoney(), DateTimeOffset.UtcNow),
                new OrderConfirmed("ord_confirmed", paymentRef, DateTimeOffset.UtcNow),
                new OrderTicketed(["TKT001"], DateTimeOffset.UtcNow)
            );
            await session.SaveChangesAsync(ct);
        }

        var provider = new RecordingBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var verifySession = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, Guid.NewGuid()),
            verifySession,
            new IFlightBookingProvider[] { provider },
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OrderNotCancellable");

        // No new OrderCancelled event appended — stream stays in Ticketed.
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);
        events.Count(e => e.Data is OrderCancelled).ShouldBe(0);
        var agg = await verifySession.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        agg!.Status.ShouldBe(BookingStatus.Ticketed);
    }

    [Fact]
    public async Task CancelAlreadyCancelled_Idempotent_NoNewEventAppended()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, initialEventCount) = await SeedCancelledStream();
        var userId = Guid.NewGuid();

        var provider = new RecordingBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        // Idempotent: returns success with Cancelled status
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe("Cancelled");

        // No new events appended — stream version unchanged
        var state = await session.Events.FetchStreamStateAsync(streamId, ct);
        ((int)state!.Version).ShouldBe(initialEventCount);

        // No notification published (idempotent return path)
        bus.Published.ShouldBeEmpty();

        // Provider not called
        provider.CancelOrderCalled.ShouldBeFalse();
    }
}
