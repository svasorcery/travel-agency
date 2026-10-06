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
using Travel.Shared.Abstractions;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

/// <summary>Verifies that a saved confirmation claim prevents a competing request from executing a second financial chain.</summary>
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
        session.Events.Append(
            streamId,
            new BookingMutationCoordinationEnabled(TimeProvider.System.GetUtcNow())
        );
        await session.SaveChangesAsync(ct);

        return streamId;
    }

    // Hold the winning capture while the competing request observes its persisted claim.
    private sealed class BarrierPaymentGateway : IPaymentGateway
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public async Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct)
        {
            Interlocked.Increment(ref _captureCalls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return Result.Success;
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
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
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
            Func<CancellationToken, Task<bool>> canDispatch,
            CancellationToken ct
        )
        {
            if (!await canDispatch(ct))
                return Error.Failure(
                    "Flights.ConfirmationFenceClosed",
                    "Synthetic supplier continuation closed."
                );
            return await ConfirmOrderAsync(providerOrderId, payment, expectedTotal, ct);
        }

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
            return new ConfirmedOrder(
                providerOrderId,
                DateTimeOffset.UtcNow,
                SupplierPaymentEvidence
                    .Create("pay_fictional", expectedTotal, SupplierPaymentKind.Balance)
                    .Value
            );
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

        var gateway = new BarrierPaymentGateway();
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
                new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
                ct
            );
        }

        var first = RunOne();
        await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        ErrorOr<ConfirmedOrderResult> second;
        try
        {
            second = await RunOne();
            second.IsError.ShouldBeTrue();
            second.FirstError.Code.ShouldBe("Flights.ConfirmationOutcomeUnknown");
        }
        finally
        {
            gateway.Release.TrySetResult();
        }
        (await first).IsError.ShouldBeFalse();
        (await RunOne()).IsError.ShouldBeFalse(); // Completed replay has no new financial effects.
        // Stream has exactly one OrderConfirmed event — the loser's append was rejected.
        await using var verifySession = _store.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);
        events.Count(e => e.Data is OrderConfirmed).ShouldBe(1);
        events.Count(e => e.Data is PaymentAuthorized).ShouldBe(1);

        gateway.CaptureCalls.ShouldBe(1);
        provider.ConfirmCalls.ShouldBe(1);
        var attempt = events.Select(e => e.Data).OfType<ConfirmationAttemptStarted>().Single();
        gateway.AuthorizeIdempotencyKeys.ShouldBe([attempt.AttemptId.ToString("N")]);
        provider.ConfirmTotals.ShouldBe([BuildMoney()]);
    }
}
