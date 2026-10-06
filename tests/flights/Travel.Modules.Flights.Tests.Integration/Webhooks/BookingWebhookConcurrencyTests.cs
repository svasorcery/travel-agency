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
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

[Trait("Category", "Integration")]
public sealed partial class BookingWebhookConcurrencyTests : IAsyncLifetime
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
        var commitGate = new WebhookCommitGate();

        async Task<Exception?> Run(Guid inboxId)
        {
            try
            {
                await using var session = _store.LightweightSession();
                session.Listeners.Add(new BeforeWebhookCommit(() => commitGate.ArriveAsync(ct)));
                await DuffelWebhookHandler.Handle(
                    new ProcessDuffelWebhookCommand(inboxId),
                    inbox,
                    session,
                    NullFlightsMetricsImpl.Instance,
                    new RecordingMartenOutbox(),
                    TimeProvider.System,
                    NullLogger<ProcessDuffelWebhookCommand>.Instance,
                    TestPii.WebhookReader,
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
        failures
            .Where(exception =>
                exception is not null && exception is not BookingWriteConflictException
            )
            .Select(exception => exception!.GetType().FullName)
            .ShouldBeEmpty("Unexpected concurrent webhook failure types.");
        failures.Count(exception => exception is null).ShouldBe(1);

        await using (var verify = _store.LightweightSession())
        {
            var events = await verify.Events.FetchStreamAsync(aggregateId, token: ct);
            events.Count.ShouldBe(5);
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
                NullFlightsMetricsImpl.Instance,
                new RecordingMartenOutbox(),
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                TestPii.WebhookReader,
                ct
            );
        }

        inbox.ProcessedCount.ShouldBe(2);
        await using var final = _store.LightweightSession();
        var finalEvents = await final.Events.FetchStreamAsync(aggregateId, token: ct);
        finalEvents.Count.ShouldBe(5);
        finalEvents.Count(candidate => candidate.Data is OrderTicketed).ShouldBe(1);
    }

    [Fact]
    public async Task Legacy_cancel_requires_terms_and_does_not_block_the_ticket_webhook_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var providerOrderId = "ord_" + Guid.NewGuid().ToString("N");
        var id = await SeedConfirmedStream(providerOrderId, owner, ct);
        var inboxId = Guid.NewGuid();
        var inbox = new InMemoryWebhookInboxStore(id, providerOrderId);
        inbox.Add(inboxId, BuildTicketPayload(providerOrderId, "TKT-RACE"));
        await using var webhookSession = _store.LightweightSession();
        await using var legacySession = _store.LightweightSession();
        var webhook = DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inboxId),
            inbox,
            webhookSession,
            NullFlightsMetricsImpl.Instance,
            new RecordingMartenOutbox(),
            TimeProvider.System,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            TestPii.WebhookReader,
            ct
        );
        var legacy = CancelOrderHandler.Handle(
            new CancelOrderCommand(id, owner),
            legacySession,
            [new SuccessfulCancelProvider()],
            NullFlightsMetricsImpl.Instance,
            new RecordingMartenOutbox(),
            TimeProvider.System,
            NullLogger<CancelOrderCommand>.Instance,
            ct
        );
        await Task.WhenAll(webhook, legacy);
        (await legacy).FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");
        inbox.ProcessedCount.ShouldBe(1);
        await using var verify = _store.LightweightSession();
        var events = await verify.Events.FetchStreamAsync(id, token: ct);
        events.Count.ShouldBe(5);
        events.Count(e => e.Data is OrderTicketed).ShouldBe(1);
        events.Count(e => e.Data is OrderCancelled).ShouldBe(0);
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

    private sealed class SuccessfulCancelProvider : IFlightBookingProvider
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
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
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

// This separate class has no database fixture/lifecycle and can run with an exact type filter.
public sealed class WebhookCommitGateTests
{
    [Fact]
    public async Task Sequential_starts_yield_until_both_contenders_reach_commit()
    {
        var gate = new WebhookCommitGate();
        var first = gate.ArriveAsync(TestContext.Current.CancellationToken);
        first.IsCompleted.ShouldBeFalse();
        var second = gate.ArriveAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second);
        first.IsCompletedSuccessfully.ShouldBeTrue();
        second.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_contender_fails_explicitly_instead_of_releasing_a_writer()
    {
        var gate = new WebhookCommitGate(TimeSpan.Zero);
        var error = await Should.ThrowAsync<TimeoutException>(() =>
            gate.ArriveAsync(TestContext.Current.CancellationToken)
        );
        error.Message.ShouldBe("Webhook contenders did not both reach the commit boundary.");
        error.InnerException.ShouldBeNull();
        await Should.ThrowAsync<TimeoutException>(() =>
            gate.ArriveAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task More_than_two_arrivals_is_a_fixture_error()
    {
        var gate = new WebhookCommitGate();
        await Task.WhenAll(
            gate.ArriveAsync(TestContext.Current.CancellationToken),
            gate.ArriveAsync(TestContext.Current.CancellationToken)
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            gate.ArriveAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Requested_cancellation_cancels_the_wait_without_releasing_a_writer()
    {
        using var cts = new CancellationTokenSource();
        var gate = new WebhookCommitGate();
        var pending = gate.ArriveAsync(cts.Token);
        await cts.CancelAsync();
        OperationCanceledException? error = null;
        try
        {
            await pending;
        }
        catch (OperationCanceledException caught)
        {
            error = caught;
        }
        error.ShouldNotBeNull();
        error.CancellationToken.ShouldBe(cts.Token);
        pending.IsCanceled.ShouldBeTrue();
    }
}

// The async pre-save rendezvous lets both callers load/append against the same stream
// version before either commit. Clock calls are intentionally unrelated to coordination.
internal sealed class WebhookCommitGate(TimeSpan? timeout = null)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(15);
    private readonly TaskCompletionSource _bothArrived = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _arrivals;

    public async Task ArriveAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var arrival = Interlocked.Increment(ref _arrivals);
        if (arrival > 2)
            throw new InvalidOperationException(
                "Webhook commit gate received more than two contenders."
            );
        if (arrival == 2)
            _bothArrived.TrySetResult();

        try
        {
            await _bothArrived.Task.WaitAsync(_timeout, ct);
        }
        catch (TimeoutException)
        {
            var failure = new TimeoutException(
                "Webhook contenders did not both reach the commit boundary."
            );
            _bothArrived.TrySetException(failure);
            throw failure;
        }
    }
}
