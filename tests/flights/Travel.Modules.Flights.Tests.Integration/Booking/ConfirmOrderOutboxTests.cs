using ErrorOr;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Marten;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;
using Wolverine.Marten;
using Wolverine.Tracking;
using Xunit;
using AutoCreate = JasperFx.AutoCreate;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

/// <summary>
/// Verifies that <see cref="OrderConfirmedNotification"/> rides the Wolverine
/// transactional outbox: it is delivered when the handler's Marten transaction
/// commits, and it is suppressed when that transaction is rejected by an
/// optimistic-concurrency conflict. The fixture mirrors apps/Travel.Host/Program.cs
/// (Marten.IntegrateWithWolverine + AutoApplyTransactions +
/// UseEntityFrameworkCoreTransactions) so the outbox wiring under test matches
/// production.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConfirmOrderOutboxTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private IHost _host = default!;
    private NotificationRecorder _recorder = default!;

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        var connectionString = _pg.GetConnectionString();

        _recorder = new NotificationRecorder();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(_recorder);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        builder.Services.AddSingleton<IFlightsMetrics>(NullFlightsMetricsImpl.Instance);

        builder.Services.AddDbContext<FlightsDbContext>(opts =>
        {
            opts.UseNpgsql(connectionString);
            opts.UseSnakeCaseNamingConvention();
        });
        builder.Services.AddScoped<IOrderReadModelProjector, OrderReadModelProjectorImpl>();
        builder.Services.AddSingleton<IPaymentGateway, SuccessPaymentGateway>();
        builder.Services.AddSingleton<IFlightBookingProvider, SuccessBookingProvider>();

        builder
            .Services.AddMarten(opts =>
            {
                opts.Connection(connectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.ConfigureFlightsBooking();
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        builder.UseWolverine(opts =>
        {
            opts.Policies.AutoApplyTransactions();
            opts.Policies.UseDurableLocalQueues();
            opts.UseEntityFrameworkCoreTransactions();
            // Don't scan the application assembly for OrderConfirmedNotification
            // listeners — they require many production services that are out of
            // scope for this outbox test. Whitelist exactly the handler types we
            // need: ConfirmOrderHandler (the SUT) and the recorder handler.
            opts.Discovery.DisableConventionalDiscovery();
            opts.Discovery.IncludeType(typeof(ConfirmOrderHandler));
            opts.Discovery.IncludeType(typeof(OrderConfirmedNotificationHandler));
            opts.Services.RunWolverineInSoloMode();
        });

        _host = builder.Build();
        await _host.StartAsync();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
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
        return Itinerary.Create(new[] { Slice.Create(new[] { seg }).Value }).Value;
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

    private async Task<Guid> SeedHeldStream()
    {
        var streamId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        using var scope = _host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();

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
                OrderId: "ord_" + Guid.NewGuid(),
                Passenger: BuildPassenger(),
                HeldUntil: DateTimeOffset.UtcNow.AddHours(2),
                HeldAt: DateTimeOffset.UtcNow
            )
        );
        await session.SaveChangesAsync(ct);
        return streamId;
    }

    [Fact]
    public async Task Confirm_publishes_notification_through_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedHeldStream();
        var userId = Guid.NewGuid();

        // Invoke through Wolverine — AutoApplyTransactions wraps the handler in a
        // Marten session transaction; IMessageBus.PublishAsync from inside the
        // handler is auto-enrolled in that session's outbox. TrackActivity waits
        // until the outgoing message has been handled by the local queue.
        await _host
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(ctx =>
                ctx.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                    new ConfirmOrderCommand(streamId, userId),
                    ct
                )
            );

        _recorder.Confirmed.ShouldContain(n => n.AggregateId == streamId && n.UserId == userId);

        // And the stream really did commit OrderConfirmed (the outbox didn't
        // deliver a "phantom" message ahead of the events).
        using var verifyScope = _host.Services.CreateScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verifySession = store.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);
        events.Count(e => e.Data is OrderConfirmed).ShouldBe(1);
    }

    [Fact]
    public async Task Confirm_rollback_suppresses_notification()
    {
        // This test pins the outbox invariant: a delivered OrderConfirmedNotification
        // must correspond to a committed OrderConfirmed event in the stream. If the
        // handler's transaction is rejected (state guard, concurrency conflict,
        // exception thrown between AppendOne and SaveChangesAsync), the notification
        // must not leak past the rollback. We exercise the easiest-to-engineer
        // rollback shape: a state-guard rejection. The handler short-circuits with
        // InvalidState before appending or publishing — no events, no notification.
        //
        // The harder rollback shapes (mid-flight exception, ConcurrencyConflict at
        // SaveChangesAsync after AppendOne+PublishAsync) are covered transitively:
        // moving PublishAsync onto the Marten session's outbox means *any* path
        // that doesn't reach SaveChangesAsync drops the buffered message.
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedHeldStream();
        var userId = Guid.NewGuid();
        _recorder.Confirmed.Clear();

        // Behind the handler's back, cancel the order. The aggregate is no longer
        // in Held when the handler loads it: the state guard returns InvalidState.
        using (var seedScope = _host.Services.CreateScope())
        {
            var store = seedScope.ServiceProvider.GetRequiredService<IDocumentStore>();
            await using var divergent = store.LightweightSession();
            divergent.Events.Append(
                streamId,
                new OrderCancelled(CancelReason.System, DateTimeOffset.UtcNow)
            );
            await divergent.SaveChangesAsync(ct);
        }

        await _host
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(ctx =>
                ctx.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                    new ConfirmOrderCommand(streamId, userId),
                    ct
                )
            );

        // No OrderConfirmed event was appended AND no notification was delivered.
        using var verifyScope = _host.Services.CreateScope();
        var verifyStore = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verifySession = verifyStore.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);
        events.Count(e => e.Data is OrderConfirmed).ShouldBe(0);

        _recorder.Confirmed.ShouldNotContain(n => n.AggregateId == streamId);
    }

    [Fact]
    public async Task Confirm_concurrency_conflict_suppresses_notification()
    {
        // Two concurrent confirms against the same Held stream: the loser raises
        // EventStreamUnexpectedMaxEventIdException at SaveChangesAsync. With the
        // outbox fix (PublishAsync before SaveChangesAsync, riding the Marten
        // session's outbox), the loser's buffered notification is rolled back.
        // Without it (fire-and-forget publish after SaveChangesAsync), the loser
        // returns its error AFTER SaveChangesAsync throws so PublishAsync is
        // unreached — but moving capture into the optimistic-write boundary in a
        // future workstream would surface the bug. We pin the invariant either way:
        // notification count for the stream == 1, even though two handlers ran.
        var ct = TestContext.Current.CancellationToken;
        var streamId = await SeedHeldStream();
        var userId = Guid.NewGuid();
        _recorder.Confirmed.Clear();

        // Use a single TrackActivity that runs both confirms in its lambda — both
        // commands are tracked under one session so the tracker waits for *both*
        // (winner + loser) to fully settle before completing.
        await _host
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(
                    async ctx =>
                    {
                        var t1 = ctx.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                            new ConfirmOrderCommand(streamId, userId),
                            ct
                        );
                        var t2 = ctx.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                            new ConfirmOrderCommand(streamId, userId),
                            ct
                        );
                        await Task.WhenAll(t1, t2);
                    }
                )
            );

        using var verifyScope = _host.Services.CreateScope();
        var verifyStore = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verifySession = verifyStore.LightweightSession();
        var events = await verifySession.Events.FetchStreamAsync(streamId, token: ct);

        var confirmedCount = events.Count(e => e.Data is OrderConfirmed);
        var notificationCount = _recorder.Confirmed.Count(n => n.AggregateId == streamId);

        confirmedCount.ShouldBe(1, "exactly one confirm must win the optimistic-write race");
        notificationCount.ShouldBe(
            confirmedCount,
            "the loser's notification must not leak past its rolled-back transaction"
        );
    }

    // ─── recorder ──────────────────────────────────────────────────────────────

    public sealed class NotificationRecorder
    {
        private readonly List<OrderConfirmedNotification> _confirmed = new();
        private readonly Lock _gate = new();

        public List<OrderConfirmedNotification> Confirmed
        {
            get
            {
                lock (_gate)
                    return _confirmed.ToList();
            }
        }

        public void Add(OrderConfirmedNotification n)
        {
            lock (_gate)
                _confirmed.Add(n);
        }
    }

    public static class OrderConfirmedNotificationHandler
    {
        [WolverineHandler]
        public static void Handle(OrderConfirmedNotification n, NotificationRecorder r) => r.Add(n);
    }

    // ─── fake collaborators ────────────────────────────────────────────────────

    private sealed class SuccessPaymentGateway : IPaymentGateway
    {
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
        ) => Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
    }

    private sealed class SuccessBookingProvider : IFlightBookingProvider
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
            string idempotencyKey,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<ConfirmedOrder>>(
                new ConfirmedOrder("ord_confirmed_" + Guid.NewGuid(), DateTimeOffset.UtcNow)
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
}
