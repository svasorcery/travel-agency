using System.Collections.Concurrent;
using System.Text.Json;
using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Handlers.Webhooks;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Travel.Shared.Abstractions;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

[Trait("Category", "Integration")]
public sealed class BookingWebhookConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    private DocumentStore _store = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        _store = DocumentStore.For(options =>
        {
            options.Connection(_pg.GetConnectionString());
            options.AutoCreateSchemaObjects = AutoCreate.All;
            FlightsModule.ConfigureMarten(options);
        });
    }

    public async ValueTask DisposeAsync()
    {
        _store.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_ticket_callbacks_append_once_and_retry_the_loser_as_noop()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var providerOrderId = "ord_" + Guid.NewGuid().ToString("N");
        var aggregateId = await SeedConfirmedStream(providerOrderId, owner, ct);
        var firstInbox = Guid.NewGuid();
        var secondInbox = Guid.NewGuid();
        var inbox = new InMemoryWebhookInboxStore(aggregateId, providerOrderId);
        inbox.Add(firstInbox, BuildTicketPayload(providerOrderId, "TKT-1"));
        inbox.Add(secondInbox, BuildTicketPayload(providerOrderId, "TKT-1"));
        using var barrier = new Barrier(2);
        var time = new FirstEventBarrierTimeProvider(barrier);

        async Task<Exception?> Run(Guid inboxId)
        {
            try
            {
                await using var session = _store.LightweightSession();
                await DuffelWebhookHandler.Handle(
                    new ProcessDuffelWebhookCommand(inboxId),
                    inbox,
                    session,
                    new NullProjector(),
                    NullFlightsMetricsImpl.Instance,
                    new RecordingMartenOutbox(),
                    time,
                    NullLogger<ProcessDuffelWebhookCommand>.Instance,
                    ct
                );
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var failures = await Task.WhenAll(Run(firstInbox), Run(secondInbox));

        await using (var verify = _store.LightweightSession())
        {
            var events = await verify.Events.FetchStreamAsync(aggregateId, token: ct);
            events.Count(candidate => candidate.Data is OrderTicketed).ShouldBe(1);
        }
        failures.Count(candidate => candidate is BookingWriteConflictException).ShouldBe(1);
        inbox.ProcessedCount.ShouldBe(1);

        var loser = inbox.UnprocessedIds.ShouldHaveSingleItem();
        await using (var retry = _store.LightweightSession())
        {
            await DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(loser),
                inbox,
                retry,
                new NullProjector(),
                NullFlightsMetricsImpl.Instance,
                new RecordingMartenOutbox(),
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                ct
            );
        }

        inbox.ProcessedCount.ShouldBe(2);
        await using var final = _store.LightweightSession();
        var finalEvents = await final.Events.FetchStreamAsync(aggregateId, token: ct);
        finalEvents.Count(candidate => candidate.Data is OrderTicketed).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_ticket_and_user_cancel_serialize_then_redecide_the_loser()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var providerOrderId = "ord_" + Guid.NewGuid().ToString("N");
        var aggregateId = await SeedConfirmedStream(providerOrderId, owner, ct);
        var inboxId = Guid.NewGuid();
        var inbox = new InMemoryWebhookInboxStore(aggregateId, providerOrderId);
        inbox.Add(inboxId, BuildTicketPayload(providerOrderId, "TKT-RACE"));
        using var barrier = new Barrier(2);
        var time = new FirstEventBarrierTimeProvider(barrier);

        async Task<Exception?> RunWebhook()
        {
            try
            {
                await using var session = _store.LightweightSession();
                await DuffelWebhookHandler.Handle(
                    new ProcessDuffelWebhookCommand(inboxId),
                    inbox,
                    session,
                    new NullProjector(),
                    NullFlightsMetricsImpl.Instance,
                    new RecordingMartenOutbox(),
                    time,
                    NullLogger<ProcessDuffelWebhookCommand>.Instance,
                    ct
                );
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        async Task<ErrorOr<CancelledOrderResult>> RunCancel()
        {
            await using var session = _store.LightweightSession();
            return await CancelOrderHandler.Handle(
                new CancelOrderCommand(aggregateId, owner),
                session,
                [new SuccessfulCancelProvider()],
                new NullProjector(),
                NullFlightsMetricsImpl.Instance,
                new RecordingMartenOutbox(),
                time,
                NullLogger<CancelOrderCommand>.Instance,
                ct
            );
        }

        var webhookTask = RunWebhook();
        var cancelTask = RunCancel();
        await Task.WhenAll(webhookTask, cancelTask);
        var webhookFailure = await webhookTask;
        var cancelResult = await cancelTask;
        var conflictCount =
            (webhookFailure is BookingWriteConflictException ? 1 : 0)
            + (
                cancelResult.IsError
                && cancelResult.FirstError.Code == "Flights.ConcurrencyConflict"
                    ? 1
                    : 0
            );
        conflictCount.ShouldBe(1);

        await using (var verify = _store.LightweightSession())
        {
            var events = await verify.Events.FetchStreamAsync(aggregateId, token: ct);
            events
                .Count(candidate => candidate.Data is OrderTicketed or OrderCancelled)
                .ShouldBe(1);
        }

        if (webhookFailure is BookingWriteConflictException)
        {
            await using var retry = _store.LightweightSession();
            await DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(inboxId),
                inbox,
                retry,
                new NullProjector(),
                NullFlightsMetricsImpl.Instance,
                new RecordingMartenOutbox(),
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                ct
            );
            inbox.ProcessedCount.ShouldBe(1);
        }
        else
        {
            await using var retry = _store.LightweightSession();
            var retryResult = await CancelOrderHandler.Handle(
                new CancelOrderCommand(aggregateId, owner),
                retry,
                [new SuccessfulCancelProvider()],
                new NullProjector(),
                NullFlightsMetricsImpl.Instance,
                new RecordingMartenOutbox(),
                TimeProvider.System,
                NullLogger<CancelOrderCommand>.Instance,
                ct
            );
            retryResult.IsError.ShouldBeTrue();
            retryResult.FirstError.Code.ShouldBe("Flights.OrderNotCancellable");
            inbox.ProcessedCount.ShouldBe(1);
        }

        await using var final = _store.LightweightSession();
        var finalEvents = await final.Events.FetchStreamAsync(aggregateId, token: ct);
        finalEvents
            .Count(candidate => candidate.Data is OrderTicketed or OrderCancelled)
            .ShouldBe(1);
    }

    private async Task<Guid> SeedConfirmedStream(
        string providerOrderId,
        Guid owner,
        CancellationToken ct
    )
    {
        var aggregateId = Guid.NewGuid();
        var amount = Money.Create(100m, CurrencyCode.Create("USD").Value).Value;
        var payment = PaymentRef.New();
        await using var session = _store.LightweightSession();
        session.Events.StartStream<BookingAggregate>(
            aggregateId,
            new OfferQuoted(
                OfferId.New(),
                BuildItinerary(),
                amount,
                DateTimeOffset.UtcNow.AddMinutes(30),
                "off_test",
                DateTimeOffset.UtcNow
            ),
            new OfferHeld(
                providerOrderId,
                BuildPassenger(),
                DateTimeOffset.UtcNow.AddHours(2),
                DateTimeOffset.UtcNow,
                owner
            ),
            new PaymentAuthorized(payment, amount, DateTimeOffset.UtcNow),
            new OrderConfirmed(providerOrderId, payment, DateTimeOffset.UtcNow)
        );
        await session.SaveChangesAsync(ct);
        return aggregateId;
    }

    private static string BuildTicketPayload(string providerOrderId, string ticketNumber) =>
        JsonSerializer.Serialize(
            new
            {
                @object = new
                {
                    id = providerOrderId,
                    documents = new[] { new { type = "ticket", unique_identifier = ticketNumber } },
                },
            }
        );

    private static Itinerary BuildItinerary()
    {
        var departure = DateTimeOffset.UtcNow.AddDays(1);
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                departure,
                departure.AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return Itinerary.Create([Slice.Create([segment]).Value]).Value;
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
                DateOnly.FromDateTime(DateTime.UtcNow)
            )
            .Value;

    private sealed class NullProjector : IOrderReadModelProjector
    {
        public Task Project(BookingAggregate agg, Guid userId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class SuccessfulCancelProvider : IFlightBookingProvider
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
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    private sealed class FirstEventBarrierTimeProvider(Barrier barrier) : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref _calls) <= 2)
                barrier.SignalAndWait(TimeSpan.FromSeconds(15));

            return DateTimeOffset.UtcNow;
        }
    }

    private sealed class InMemoryWebhookInboxStore(Guid aggregateId, string providerOrderId)
        : IWebhookInboxStore
    {
        private readonly ConcurrentDictionary<Guid, WebhookInboxEntry> _entries = new();

        public int ProcessedCount => _entries.Values.Count(entry => entry.ProcessedAt is not null);

        public IReadOnlyList<Guid> UnprocessedIds =>
            _entries
                .Values.Where(entry => entry.ProcessedAt is null)
                .Select(entry => entry.Id)
                .ToList();

        public void Add(Guid inboxId, string payload) =>
            _entries[inboxId] = new WebhookInboxEntry(
                inboxId,
                "order.created",
                payload,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                null
            );

        public Task<WebhookInboxEntry?> FindAsync(Guid inboxId, CancellationToken ct) =>
            Task.FromResult(_entries.TryGetValue(inboxId, out var entry) ? entry : null);

        public Task MarkProcessedAsync(
            Guid inboxId,
            DateTimeOffset processedAt,
            CancellationToken ct
        )
        {
            _entries.AddOrUpdate(
                inboxId,
                static _ => throw new InvalidOperationException("Inbox entry disappeared."),
                (_, entry) => entry with { ProcessedAt = processedAt }
            );
            return Task.CompletedTask;
        }

        public Task<Guid> FindAggregateIdByProviderOrderIdAsync(
            string candidate,
            CancellationToken ct
        ) => Task.FromResult(candidate == providerOrderId ? aggregateId : Guid.Empty);
    }
}
