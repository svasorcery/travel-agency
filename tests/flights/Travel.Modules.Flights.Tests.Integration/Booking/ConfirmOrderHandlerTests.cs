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
public sealed class ConfirmOrderHandlerTests : IAsyncLifetime
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
                PhoneNumber.Create("+79161234567").Value
            )
            .Value;

    private static Money BuildMoney() => Money.Create(5420m, Rub).Value;

    /// <summary>Seeds OfferQuoted + OfferHeld events and returns the stream id.</summary>
    private async Task<Guid> SeedHeldStream()
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

        session.Events.Append(
            streamId,
            new OfferHeld(
                OrderId: "ord_" + Guid.NewGuid(),
                Passenger: BuildPassenger(),
                HeldUntil: DateTimeOffset.UtcNow.AddHours(2),
                HeldAt: DateTimeOffset.UtcNow
            )
        );
        await session.SaveChangesAsync(ct);

        return streamId;
    }

    private async Task<Guid> SeedQuotedOnlyStream()
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

    // ─── fake collaborators ─────────────────────────────────────────────────────

    private sealed class SuccessPaymentGateway : IPaymentGateway
    {
        public List<string> RefundCalls { get; } = new();

        public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
            Money amount,
            string idempotencyKey,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());

        public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct) =>
            Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<RefundRef>> RefundAsync(
            PaymentRef payment,
            Money amount,
            CancellationToken ct
        )
        {
            RefundCalls.Add(payment.Value.ToString("N"));
            return Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
        }
    }

    private sealed class FailingCaptureGateway : IPaymentGateway
    {
        public bool RefundCalled { get; private set; }

        public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
            Money amount,
            string idempotencyKey,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());

        public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct) =>
            Task.FromResult<ErrorOr<Success>>(
                Error.Failure("Payment.CaptureFailed", "Card declined")
            );

        public Task<ErrorOr<RefundRef>> RefundAsync(
            PaymentRef payment,
            Money amount,
            CancellationToken ct
        )
        {
            RefundCalled = true;
            return Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
        }
    }

    private sealed class SuccessBookingProvider(string confirmedOrderId) : IFlightBookingProvider
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
        ) =>
            Task.FromResult<ErrorOr<ConfirmedOrder>>(
                new ConfirmedOrder(confirmedOrderId, DateTimeOffset.UtcNow)
            );

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    private sealed class FailingBookingProvider : IFlightBookingProvider
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
        ) =>
            Task.FromResult<ErrorOr<ConfirmedOrder>>(
                Error.Failure("Provider.ConfirmFailed", "Provider error")
            );

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    private sealed class RecordingMessageBus : IMessageBus
    {
        public List<object> Published { get; } = new();

        public string? TenantId { get; set; }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => throw new NotImplementedException();
    }

    private OrderReadModelProjectorImpl CreateProjector() => new OrderReadModelProjectorImpl(_db);

    private static readonly IFlightsMetrics NullMetrics = new NullFlightsMetrics();

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_ConfirmsOrder_StreamIsConfirmed_ReadModelExists_NotificationPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedHeldStream();
        var userId = Guid.NewGuid();

        var gateway = new SuccessPaymentGateway();
        var provider = new SuccessBookingProvider("ord_confirmed_" + Guid.NewGuid());
        var bus = new RecordingMessageBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await ConfirmOrderHandler.Handle(
            new ConfirmOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            gateway,
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<ConfirmOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe("Confirmed");
        result.Value.AggregateId.ShouldBe(streamId);

        // Stream in Confirmed state
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Confirmed);

        // Read model row exists
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Confirmed");

        // Notification published
        bus.Published.OfType<OrderConfirmedNotification>()
            .ShouldHaveSingleItem()
            .AggregateId.ShouldBe(streamId);
    }

    [Fact]
    public async Task CaptureFails_StreamIsCancelled_RefundCalled_ReturnPaymentFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedHeldStream();
        var userId = Guid.NewGuid();

        var gateway = new FailingCaptureGateway();
        var provider = new SuccessBookingProvider("wont_be_called");
        var bus = new RecordingMessageBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await ConfirmOrderHandler.Handle(
            new ConfirmOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            gateway,
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<ConfirmOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PaymentFailed");

        // Stream cancelled
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Cancelled);

        // Refund was called
        gateway.RefundCalled.ShouldBeTrue();

        // Read model updated to Cancelled
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task ProviderConfirmFails_StreamIsCancelled_ReturnPaymentFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedHeldStream();
        var userId = Guid.NewGuid();

        var gateway = new SuccessPaymentGateway();
        var provider = new FailingBookingProvider();
        var bus = new RecordingMessageBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await ConfirmOrderHandler.Handle(
            new ConfirmOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            gateway,
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<ConfirmOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PaymentFailed");

        // Stream cancelled
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Cancelled);

        // Refund attempted (best-effort)
        gateway.RefundCalls.ShouldNotBeEmpty();

        // Read model updated
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task WrongState_OfferQuoted_ReturnsInvalidState_NoEventsAppended()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedQuotedOnlyStream();
        var userId = Guid.NewGuid();

        var gateway = new SuccessPaymentGateway();
        var provider = new SuccessBookingProvider("wont_be_called");
        var bus = new RecordingMessageBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var projector = CreateProjector();

        await using var session = _store.LightweightSession();
        var result = await ConfirmOrderHandler.Handle(
            new ConfirmOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            gateway,
            projector,
            NullMetrics,
            bus,
            time,
            NullLogger<ConfirmOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.InvalidState");

        // Aggregate still OfferQuoted (no additional events)
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.OfferQuoted);
    }
}

file sealed class NullFlightsMetrics : IFlightsMetrics
{
    public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

    public void RecordSearchError(string provider) { }

    public void RecordPaymentOutcome(bool success) { }

    public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

    public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

    public void RecordWebhookReceived(string eventType) { }

    public void RecordWebhookProcessingLag(double ms, string eventType) { }
}
