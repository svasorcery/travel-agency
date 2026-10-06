using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
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
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Shared.Abstractions;
using Travel.Tests.Fixtures;
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
    private async Task<(Guid StreamId, string ProviderOrderId)> SeedConfirmedStream(
        Guid ownerUserId
    )
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
                HeldAt: DateTimeOffset.UtcNow,
                OwnerUserId: ownerUserId
            ),
            new PaymentAuthorized(paymentRef, BuildMoney(), DateTimeOffset.UtcNow),
            new OrderConfirmed(providerOrderId, paymentRef, DateTimeOffset.UtcNow)
        );
        await session.SaveChangesAsync(ct);
        return (streamId, providerOrderId);
    }

    /// <summary>Seeds a stream already in Cancelled state.</summary>
    private async Task<(Guid StreamId, int EventCount)> SeedCancelledStream(Guid ownerUserId)
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
            new OfferHeld(
                "ord_" + Guid.NewGuid(),
                BuildPassenger(),
                DateTimeOffset.UtcNow.AddHours(2),
                DateTimeOffset.UtcNow,
                ownerUserId
            ),
            new OrderCancelled(CancelReason.User, DateTimeOffset.UtcNow)
        );
        await session.SaveChangesAsync(ct);

        // Return initial event count for idempotency assertion
        var state = await session.Events.FetchStreamStateAsync(streamId, ct);
        return (streamId, (int)state!.Version);
    }

    // ─── fake collaborators ─────────────────────────────────────────────────────

    private async Task<Guid> SeedHeldStream(Guid owner, string? providerOrder = "ord_fixture")
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var session = _store.LightweightSession();
        session.Events.StartStream<BookingAggregate>(
            id,
            new OfferQuoted(
                OfferId.New(),
                BuildItinerary(),
                BuildMoney(),
                now.AddHours(1),
                "off_fixture",
                now
            ),
            new OfferHeld(providerOrder!, BuildPassenger(), now.AddHours(1), now, owner)
        );
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private sealed class RecordingBookingProvider : IFlightBookingProvider
    {
        public Error? CancellationError { get; init; }
        public bool CancelOrderCalled { get; private set; }
        public string? CancelledOrderId { get; private set; }

        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string providerOrderId,
            Money expectedTotal,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct)
        {
            CancelOrderCalled = true;
            CancelledOrderId = providerOrderId;
            return Task.FromResult<ErrorOr<Success>>(
                CancellationError is { } error ? error : Result.Success
            );
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
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string providerOrderId,
            Money expectedTotal,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
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

    private static readonly IFlightsMetrics NullMetrics = NullFlightsMetricsImpl.Instance;

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provider_rejection_leaves_stream_and_outbox_unchanged(bool confirmed)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = Guid.NewGuid();
        var id = confirmed
            ? (await SeedConfirmedStream(user)).StreamId
            : await SeedHeldStream(user);
        var provider = new RecordingBookingProvider
        {
            CancellationError = FlightsErrors.OrderNotCancellable("fixture rejection"),
        };
        var outbox = NewRecordingBus();
        await using var session = _store.LightweightSession();
        var before = await session.Events.FetchStreamStateAsync(id, ct);
        var result = await CancelOrderHandler.Handle(
            new(id, user),
            session,
            [provider],
            NullMetrics,
            outbox,
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");
        var after = await session.Events.FetchStreamStateAsync(id, ct);
        after!.Version.ShouldBe(before!.Version);
        (
            await session.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct)
        )!.Status.ShouldBe(confirmed ? BookingStatus.Confirmed : BookingStatus.Held);
        outbox.Published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Missing_provider_order_cannot_become_cancelled(string? providerOrder)
    {
        var user = Guid.NewGuid();
        var id = await SeedHeldStream(user, providerOrder);
        var outbox = NewRecordingBus();
        var provider = new RecordingBookingProvider();
        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new(id, user),
            session,
            [provider],
            NullMetrics,
            outbox,
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");
        provider.CancelOrderCalled.ShouldBeFalse();
        outbox.Published.ShouldBeEmpty();
        (
            await session.Events.AggregateStreamAsync<BookingAggregate>(
                id,
                token: TestContext.Current.CancellationToken
            )
        )!.Status.ShouldBe(BookingStatus.Held);
    }

    [Fact]
    public async Task Legacy_held_cancellation_requires_current_terms_before_any_effect()
    {
        var user = Guid.NewGuid();
        var id = await SeedHeldStream(user);
        var provider = new RecordingBookingProvider();
        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new(id, user),
            session,
            [provider],
            NullMetrics,
            NewRecordingBus(),
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");
        provider.CancelOrderCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Group_cancellation_snapshot_preserves_event_count()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var binding = TestPii.Binding(2);
        var command = Travel.Tests.Fixtures.TestPii.HoldCommand(
            id,
            owner,
            BuildPassenger(),
            binding
        );
        await using (var seed = _store.LightweightSession())
        {
            seed.Events.StartStream<BookingAggregate>(
                id,
                new OfferQuoted(
                    OfferId.New(),
                    BuildItinerary(),
                    BuildMoney(),
                    now.AddHours(1),
                    "off_group",
                    now,
                    QuoteBinding: binding
                ),
                new OfferHeldV3(
                    "ord_group",
                    command.ProtectedPassengerParty,
                    now.AddHours(1),
                    now,
                    owner,
                    binding.Revision,
                    2
                )
            );
            seed.Events.Append(id, new OrderCancelled(CancelReason.User, now));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new(id, owner),
            session,
            [new RecordingBookingProvider()],
            NullMetrics,
            NewRecordingBus(),
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Snapshot.PassengerCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_noop_remains_owner_scoped(bool refunded)
    {
        var user = Guid.NewGuid();
        var (id, _) = await SeedCancelledStream(user);
        await using var session = _store.LightweightSession();
        if (refunded)
        {
            session.Events.Append(
                id,
                new OrderRefunded(
                    RefundRef.New(),
                    BuildMoney(),
                    RefundInitiator.Airline,
                    DateTimeOffset.UtcNow
                )
            );
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var before = await session.Events.FetchStreamStateAsync(
            id,
            TestContext.Current.CancellationToken
        );
        var outbox = NewRecordingBus();
        var provider = new RecordingBookingProvider();
        var denied = await CancelOrderHandler.Handle(
            new(id, Guid.NewGuid()),
            session,
            [provider],
            NullMetrics,
            outbox,
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            TestContext.Current.CancellationToken
        );
        denied.FirstError.Code.ShouldBe("Flights.OfferNotFound");
        var noop = await CancelOrderHandler.Handle(
            new(id, user),
            session,
            [provider],
            NullMetrics,
            outbox,
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            TestContext.Current.CancellationToken
        );
        noop.IsError.ShouldBeFalse();
        noop.Value.Status.ShouldBe(refunded ? "Refunded" : "Cancelled");
        (
            await session.Events.FetchStreamStateAsync(id, TestContext.Current.CancellationToken)
        )!.Version.ShouldBe(before!.Version);
        provider.CancelOrderCalled.ShouldBeFalse();
        outbox.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Legacy_confirmed_cancel_does_not_call_provider_commit_project_or_notify()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (streamId, _) = await SeedConfirmedStream(userId);

        var provider = new RecordingBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");
        provider.CancelOrderCalled.ShouldBeFalse();
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg!.Status.ShouldBe(BookingStatus.Confirmed);
        // Handler leaves EF unchanged until durable reconciliation.
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldBeNull();
        bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelFromOwnerlessQuoted_fails_closed_without_provider_or_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedQuotedStream();
        var userId = Guid.NewGuid();

        // NoOpBookingProvider will throw if CancelOrderAsync is called
        var provider = new NoOpBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferNotFound");

        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.OfferQuoted);
    }

    [Fact]
    public async Task Cancel_on_ticketed_order_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
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
                    DateTimeOffset.UtcNow,
                    userId
                ),
                new PaymentAuthorized(paymentRef, BuildMoney(), DateTimeOffset.UtcNow),
                new OrderConfirmed("ord_confirmed", paymentRef, DateTimeOffset.UtcNow),
                new OrderTicketed(new EquatableArray<string>(["TKT001"]), DateTimeOffset.UtcNow)
            );
            await session.SaveChangesAsync(ct);
        }

        var provider = new RecordingBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using var verifySession = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            verifySession,
            new IFlightBookingProvider[] { provider },
            NullMetrics,
            bus,
            time,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");

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
        var userId = Guid.NewGuid();
        var (streamId, initialEventCount) = await SeedCancelledStream(userId);

        var provider = new RecordingBookingProvider();
        var bus = NewRecordingBus();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, userId),
            session,
            new IFlightBookingProvider[] { provider },
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

    [Fact]
    public async Task Cancel_with_mismatched_owner_fails_before_provider_and_state_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, _) = await SeedConfirmedStream(Guid.NewGuid());
        var provider = new RecordingBookingProvider();

        await using var session = _store.LightweightSession();
        var result = await CancelOrderHandler.Handle(
            new CancelOrderCommand(streamId, Guid.NewGuid()),
            session,
            [provider],
            NullMetrics,
            NewRecordingBus(),
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferNotFound");
        provider.CancelOrderCalled.ShouldBeFalse();
    }
}
