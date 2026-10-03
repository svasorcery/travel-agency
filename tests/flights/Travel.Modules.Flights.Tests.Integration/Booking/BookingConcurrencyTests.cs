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
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

/// <summary>
/// Verifies that two concurrent <see cref="ConfirmOrderCommand"/> against the same
/// <c>Held</c> stream cannot both append <c>OrderConfirmed</c>: Marten's optimistic
/// concurrency must reject the loser, and the loser must observe
/// <c>Flights.ConcurrencyConflict</c>.
/// <para>
/// External effects happen before the optimistic event commit; both callers may invoke them.
/// This test proves one committed OrderConfirmed, not exactly-once financial effects.
/// The wallet is test-only and no supplier deduplication guarantee is assumed.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class BookingConcurrencyTests : IAsyncLifetime
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

    private async Task<Guid> SeedHeldStream(Guid ownerUserId)
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
                HeldAt: DateTimeOffset.UtcNow,
                OwnerUserId: ownerUserId
            )
        );
        await session.SaveChangesAsync(ct);

        return streamId;
    }

    /// <summary>
    /// A payment gateway that uses a Barrier to force both concurrent confirm tasks
    /// to fully load (and authorize) the stream before either can capture and commit.
    /// This guarantees both observe the same expected-version snapshot, so the second
    /// commit must lose the optimistic-concurrency race.
    /// Records every idempotency key passed to <see cref="AuthorizeAsync"/> so tests can
    /// assert the key is stable across retries (production-safety invariant).
    /// </summary>
    private sealed class BarrierPaymentGateway(Barrier barrier) : IPaymentGateway
    {
        private int _captureCalls;
        public int CaptureCalls => Volatile.Read(ref _captureCalls);

        private readonly System.Collections.Concurrent.ConcurrentBag<string> _authorizeIdempotencyKeys =
            new();

        /// <summary>
        /// All idempotency keys passed to <see cref="AuthorizeAsync"/> across every call.
        /// Used to assert key stability across concurrent retries.
        /// </summary>
        public IReadOnlyCollection<string> AuthorizeIdempotencyKeys => _authorizeIdempotencyKeys;

        public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
            Money amount,
            string idempotencyKey,
            CancellationToken ct
        )
        {
            _authorizeIdempotencyKeys.Add(idempotencyKey);
            return Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());
        }

        public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct)
        {
            // Block until both tasks have authorized — guarantees neither has committed.
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            Interlocked.Increment(ref _captureCalls);
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<RefundRef>> RefundAsync(
            PaymentRef payment,
            Money amount,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
    }

    private sealed class CountingBookingProvider : IFlightBookingProvider
    {
        private int _confirmCalls;
        public int ConfirmCalls => Volatile.Read(ref _confirmCalls);

        public System.Collections.Concurrent.ConcurrentBag<Money> ConfirmTotals { get; } = new();

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

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string providerOrderId,
            Money expectedTotal,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public async Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            CancellationToken ct
        )
        {
            await Task.Yield();
            ConfirmTotals.Add(expectedTotal);
            Interlocked.Increment(ref _confirmCalls);
            return new ConfirmedOrder("ord_confirmed_" + Guid.NewGuid(), DateTimeOffset.UtcNow);
        }

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Concurrent_confirm_appends_only_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var streamId = await SeedHeldStream(userId);

        using var barrier = new Barrier(participantCount: 2);
        var gateway = new BarrierPaymentGateway(barrier);
        var provider = new CountingBookingProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        async Task<ErrorOr<ConfirmedOrderResult>> RunOne()
        {
            // Each handler invocation uses its own session — mimicking two concurrent
            // HTTP requests with distinct idempotency keys hitting the same stream.
            await using var session = _store.LightweightSession();
            return await ConfirmOrderHandler.Handle(
                new ConfirmOrderCommand(streamId, userId),
                session,
                new IFlightBookingProvider[] { provider },
                gateway,
                NullFlightsMetricsImpl.Instance,
                new RecordingMartenOutbox(),
                time,
                NullLogger<ConfirmOrderCommand>.Instance,
                ct
            );
        }

        var t1 = Task.Run(RunOne, ct);
        var t2 = Task.Run(RunOne, ct);
        var results = await Task.WhenAll(t1, t2);

        var successes = results.Count(r => !r.IsError);
        var conflicts = results.Count(r =>
            r.IsError && r.FirstError.Code == "Flights.ConcurrencyConflict"
        );
        successes.ShouldBe(1, "exactly one confirm must win");
        conflicts.ShouldBe(1, "the loser must report Flights.ConcurrencyConflict");

        // Stream has exactly one OrderConfirmed event — the loser's append was rejected.
        await using var verifySession = _store.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);
        events.Count(e => e.Data is OrderConfirmed).ShouldBe(1);
        events.Count(e => e.Data is PaymentAuthorized).ShouldBe(1);

        // Capture and provider-confirm are called twice — they happen *before* the
        // commit boundary in the current handler shape. No automatic compensation
        // is attempted. What this test pins
        // is that the *committed event log* has exactly one OrderConfirmed: domain
        // state cannot diverge from the race outcome.
        gateway.CaptureCalls.ShouldBe(2);
        provider.ConfirmCalls.ShouldBe(2);

        // Preserve the existing stable test-wallet authorization key. It supplies no
        // supplier deduplication or financial guarantee across clients/server restarts.
        var expectedKey = streamId.ToString("N");
        gateway.AuthorizeIdempotencyKeys.Count.ShouldBe(
            2,
            "both concurrent handlers must have called AuthorizeAsync"
        );
        gateway.AuthorizeIdempotencyKeys.ShouldAllBe(
            k => k == expectedKey,
            "every Authorize call must use AggregateId.ToString(\"N\") as the idempotency key"
        );

        // Both calls carry the exact accepted total; the commit race does not deduplicate supplier writes.
        provider.ConfirmTotals.Count.ShouldBe(2);
        provider.ConfirmTotals.ShouldAllBe(amount => amount == BuildMoney());
    }
}
