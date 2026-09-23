using System.Text.Json;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Handlers.Webhooks;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

[Trait("Category", "Integration")]
public sealed class DuffelWebhookHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private DocumentStore _store = default!;
    private FlightsDbContext _db = default!;
    private readonly Guid _userId = Guid.NewGuid();

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

    /// <summary>
    /// Seeds OfferQuoted + OfferHeld + OrderConfirmed events and returns (streamId, providerOrderId).
    /// </summary>
    private async Task<(Guid StreamId, string ProviderOrderId)> SeedConfirmedStream(
        bool includeOwner = true
    )
    {
        var streamId = Guid.NewGuid();
        var providerOrderId = "ord_duffel_" + Guid.NewGuid().ToString("N");
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
                OrderId: providerOrderId,
                Passenger: BuildPassenger(),
                HeldUntil: DateTimeOffset.UtcNow.AddHours(2),
                HeldAt: DateTimeOffset.UtcNow,
                OwnerUserId: includeOwner ? _userId : null
            )
        );
        await session.SaveChangesAsync(ct);

        session.Events.Append(
            streamId,
            new OrderConfirmed(providerOrderId, PaymentRef.New(), DateTimeOffset.UtcNow)
        );
        await session.SaveChangesAsync(ct);

        return (streamId, providerOrderId);
    }

    /// <summary>Seeds an OrderReadModelEntity row for the given stream and returns the userId.</summary>
    private async Task<Guid> SeedReadModel(Guid aggregateId, string providerOrderId)
    {
        var userId = _userId;
        var ct = TestContext.Current.CancellationToken;

        _db.Orders.Add(
            new OrderReadModelEntity
            {
                Id = Guid.NewGuid(),
                AggregateId = aggregateId,
                UserId = userId,
                ProviderOrderId = providerOrderId,
                Status = "Confirmed",
                TotalAmount = 5420m,
                Currency = "RUB",
                ItineraryJson = "{}",
                PassengerInfoJson = "{}",
                TicketNumbers = Array.Empty<string>(),
                BookedAt = DateTimeOffset.UtcNow,
            }
        );
        await _db.SaveChangesAsync(ct);
        return userId;
    }

    private WebhookInboxEntity CreateInboxRow(
        string eventType,
        string rawPayload,
        DateTimeOffset? processedAt = null
    )
    {
        var row = new WebhookInboxEntity
        {
            Id = Guid.NewGuid(),
            Source = "duffel",
            EventId = "wh_" + Guid.NewGuid().ToString("N"),
            EventType = eventType,
            RawPayload = rawPayload,
            Signature = "sha256=test",
            ReceivedAt = DateTimeOffset.UtcNow,
            ProcessedAt = processedAt,
        };
        _db.WebhookInbox.Add(row);
        return row;
    }

    private static string BuildOrderCreatedPayload(string duffelOrderId, string ticketNumber) =>
        JsonSerializer.Serialize(
            new
            {
                id = "wh_evt_" + Guid.NewGuid().ToString("N"),
                type = "order.created",
                created_at = DateTimeOffset.UtcNow,
                @object = new
                {
                    id = duffelOrderId,
                    documents = new[] { new { type = "ticket", unique_identifier = ticketNumber } },
                },
            }
        );

    private static string BuildAirlineCancelledPayload(string duffelOrderId) =>
        JsonSerializer.Serialize(
            new
            {
                id = "wh_evt_" + Guid.NewGuid().ToString("N"),
                type = "order.airline_initiated_change.cancelled",
                created_at = DateTimeOffset.UtcNow,
                @object = new { id = duffelOrderId },
            }
        );

    private static string BuildUnknownTypePayload(string duffelOrderId) =>
        JsonSerializer.Serialize(
            new
            {
                id = "wh_evt_" + Guid.NewGuid().ToString("N"),
                type = "order.something_unknown",
                created_at = DateTimeOffset.UtcNow,
                @object = new { id = duffelOrderId },
            }
        );

    private static string BuildAirlineInitiatedChangePayload(string duffelOrderId) =>
        JsonSerializer.Serialize(
            new
            {
                id = "wh_evt_" + Guid.NewGuid().ToString("N"),
                // Non-cancelled airline-initiated change (e.g. schedule change).
                type = "order.airline_initiated_change",
                created_at = DateTimeOffset.UtcNow,
                @object = new { id = duffelOrderId },
            }
        );

    private static readonly IFlightsMetrics NullMetrics = new NullFlightsMetrics();

    // ─── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OrderCreated_WithTicket_StreamTicketed_ReadModelDeferred_InboxMarkedProcessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        var ticketNumber = "TKT-" + Guid.NewGuid().ToString("N")[..8];
        var inbox = CreateInboxRow(
            "order.created",
            BuildOrderCreatedPayload(providerOrderId, ticketNumber)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        await using var session = _store.LightweightSession();
        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        // Stream ends in Ticketed
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Ticketed);
        agg.TicketNumbers.ShouldContain(ticketNumber);

        // Handler leaves EF unchanged until durable reconciliation.
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Confirmed");
        row.TicketNumbers.ShouldBeEmpty();

        // Inbox marked processed
        var inboxRow = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.WebhookInbox,
            x => x.Id == inbox.Id,
            ct
        );
        inboxRow.ShouldNotBeNull();
        inboxRow.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AirlineInitiatedCancellation_StreamRefunded_ReadModelDeferred_InboxMarkedProcessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        var inbox = CreateInboxRow(
            "order.airline_initiated_change.cancelled",
            BuildAirlineCancelledPayload(providerOrderId)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        await using var session = _store.LightweightSession();
        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        // Stream ends in Refunded
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Refunded);

        // Handler leaves EF unchanged until durable reconciliation.
        var row = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Orders,
            o => o.AggregateId == streamId,
            ct
        );
        row.ShouldNotBeNull();
        row.Status.ShouldBe("Confirmed");
        row.RefundedAt.ShouldBeNull();

        // Inbox marked processed
        var inboxRow = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.WebhookInbox,
            x => x.Id == inbox.Id,
            ct
        );
        inboxRow.ShouldNotBeNull();
        inboxRow.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Idempotency_SecondRun_NoOp_StreamUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        var ticketNumber = "TKT-IDEM-" + Guid.NewGuid().ToString("N")[..8];
        var inbox = CreateInboxRow(
            "order.created",
            BuildOrderCreatedPayload(providerOrderId, ticketNumber)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        // First run
        await using var session1 = _store.LightweightSession();
        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session1,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        var afterFirstRun = await session1.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        afterFirstRun.ShouldNotBeNull();
        afterFirstRun.Status.ShouldBe(BookingStatus.Ticketed);
        var versionAfterFirst = afterFirstRun.Version;

        // Second run — should no-op
        await using var session2 = _store.LightweightSession();
        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session2,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        var afterSecondRun = await session2.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        afterSecondRun.ShouldNotBeNull();
        afterSecondRun.Version.ShouldBe(versionAfterFirst); // no new events appended
        afterSecondRun.Status.ShouldBe(BookingStatus.Ticketed);
    }

    [Fact]
    public async Task Airline_initiated_change_records_metric_and_no_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        var inbox = CreateInboxRow(
            "order.airline_initiated_change",
            BuildAirlineInitiatedChangePayload(providerOrderId)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);
        var metrics = new NullFlightsMetrics();

        await using var session = _store.LightweightSession();
        var versionBefore = (
            await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct)
        )!.Version;

        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session,
            metrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        // Counter incremented exactly once.
        metrics.AirlineInitiatedChangeCount.ShouldBe(1);

        // No domain event appended — version unchanged.
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Version.ShouldBe(versionBefore);
        agg.Status.ShouldBe(BookingStatus.Confirmed);

        // Inbox row marked processed.
        var inboxRow = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.WebhookInbox,
            x => x.Id == inbox.Id,
            ct
        );
        inboxRow.ShouldNotBeNull();
        inboxRow.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task UnknownEventType_InboxMarkedProcessed_NoDomainEventAppended()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        var inbox = CreateInboxRow(
            "order.something_unknown",
            BuildUnknownTypePayload(providerOrderId)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        await using var session = _store.LightweightSession();
        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        // Aggregate unchanged — still Confirmed
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Confirmed);

        // Inbox marked processed
        var inboxRow = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.WebhookInbox,
            x => x.Id == inbox.Id,
            ct
        );
        inboxRow.ShouldNotBeNull();
        inboxRow.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Ticketed_webhook_on_already_ticketed_stream_is_noop()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        // Append a prior OrderTicketed so the stream is already in the terminal
        // Ticketed state. A second order.created webhook with a DIFFERENT event.id
        // (e.g. a Duffel re-delivery for a stream that fast-tracked to Ticketed)
        // must NOT append a second OrderTicketed.
        await using (var seedSession = _store.LightweightSession())
        {
            seedSession.Events.Append(
                streamId,
                new OrderTicketed(new[] { "TKT-ORIGINAL" }, DateTimeOffset.UtcNow)
            );
            await seedSession.SaveChangesAsync(ct);
        }

        var ticketNumber = "TKT-DUP-" + Guid.NewGuid().ToString("N")[..8];
        var inbox = CreateInboxRow(
            "order.created",
            BuildOrderCreatedPayload(providerOrderId, ticketNumber)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        await using var session = _store.LightweightSession();
        var versionBefore = (
            await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct)
        )!.Version;

        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        // No new event appended — version is unchanged, ticket numbers still the originals.
        var afterAgg = await session.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        afterAgg.ShouldNotBeNull();
        afterAgg.Version.ShouldBe(versionBefore);
        afterAgg.Status.ShouldBe(BookingStatus.Ticketed);
        afterAgg.TicketNumbers.ShouldBe(new[] { "TKT-ORIGINAL" });

        // Inbox is still marked processed — we do NOT want Duffel to keep retrying
        // an event we deliberately ignored as a no-op.
        var inboxRow = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.WebhookInbox,
            x => x.Id == inbox.Id,
            ct
        );
        inboxRow.ShouldNotBeNull();
        inboxRow.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refund_webhook_on_terminal_stream_is_noop()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);

        // Drive the stream into the terminal Refunded state before we deliver the
        // airline-initiated-cancellation webhook. A second refund webhook for the
        // same Duffel order must NOT append a second OrderRefunded.
        await using (var seedSession = _store.LightweightSession())
        {
            seedSession.Events.Append(
                streamId,
                new OrderRefunded(
                    RefundRef.New(),
                    BuildMoney(),
                    RefundInitiator.Airline,
                    DateTimeOffset.UtcNow
                )
            );
            await seedSession.SaveChangesAsync(ct);
        }

        var inbox = CreateInboxRow(
            "order.airline_initiated_change.cancelled",
            BuildAirlineCancelledPayload(providerOrderId)
        );
        await _db.SaveChangesAsync(ct);

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        await using var session = _store.LightweightSession();
        var versionBefore = (
            await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct)
        )!.Version;

        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            inboxStore,
            session,
            NullMetrics,
            new RecordingMartenOutbox(),
            time,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            ct
        );

        // No new event appended — version is unchanged, status still Refunded.
        var afterAgg = await session.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        afterAgg.ShouldNotBeNull();
        afterAgg.Version.ShouldBe(versionBefore);
        afterAgg.Status.ShouldBe(BookingStatus.Refunded);

        // Inbox is still marked processed.
        var inboxRow = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.WebhookInbox,
            x => x.Id == inbox.Id,
            ct
        );
        inboxRow.ShouldNotBeNull();
        inboxRow.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Ticket_webhook_on_ownerless_confirmed_stream_fails_before_event_or_notification()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream(includeOwner: false);
        await SeedReadModel(streamId, providerOrderId);
        var inbox = CreateInboxRow(
            "order.created",
            BuildOrderCreatedPayload(providerOrderId, "TKT-OWNERLESS")
        );
        await _db.SaveChangesAsync(ct);
        var outbox = new RecordingMartenOutbox();

        await using var session = _store.LightweightSession();
        await Should.ThrowAsync<BookingSourceOwnershipMissingException>(() =>
            DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(inbox.Id),
                new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance),
                session,
                NullMetrics,
                outbox,
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                ct
            )
        );

        var events = await session.Events.FetchStreamAsync(streamId, token: ct);
        events.ShouldNotContain(candidate => candidate.Data is OrderTicketed);
        outbox.Published.ShouldBeEmpty();
        _db.WebhookInbox.Single(candidate => candidate.Id == inbox.Id).ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Refund_webhook_on_ownerless_confirmed_stream_fails_before_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream(includeOwner: false);
        await SeedReadModel(streamId, providerOrderId);
        var inbox = CreateInboxRow(
            "order.airline_initiated_change.cancelled",
            BuildAirlineCancelledPayload(providerOrderId)
        );
        await _db.SaveChangesAsync(ct);
        var outbox = new RecordingMartenOutbox();

        await using var session = _store.LightweightSession();
        await Should.ThrowAsync<BookingSourceOwnershipMissingException>(() =>
            DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(inbox.Id),
                new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance),
                session,
                NullMetrics,
                outbox,
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                ct
            )
        );

        var events = await session.Events.FetchStreamAsync(streamId, token: ct);
        events.ShouldNotContain(candidate => candidate.Data is OrderRefunded);
        outbox.Published.ShouldBeEmpty();
        _db.WebhookInbox.Single(candidate => candidate.Id == inbox.Id).ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Failure_after_ticket_commit_retries_as_noop_then_marks_inbox_processed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (streamId, providerOrderId) = await SeedConfirmedStream();
        await SeedReadModel(streamId, providerOrderId);
        var inbox = CreateInboxRow(
            "order.created",
            BuildOrderCreatedPayload(providerOrderId, "TKT-POST-COMMIT")
        );
        await _db.SaveChangesAsync(ct);
        var firstOutbox = new RecordingMartenOutbox();
        var inboxStore = new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance);

        await using (var firstSession = _store.LightweightSession())
        {
            await Should.ThrowAsync<InjectedAcknowledgementFailureException>(() =>
                DuffelWebhookHandler.Handle(
                    new ProcessDuffelWebhookCommand(inbox.Id),
                    new FailingAcknowledgement(inboxStore),
                    firstSession,
                    NullMetrics,
                    firstOutbox,
                    TimeProvider.System,
                    NullLogger<ProcessDuffelWebhookCommand>.Instance,
                    ct
                )
            );
        }

        await using (var verifyCommit = _store.LightweightSession())
        {
            var events = await verifyCommit.Events.FetchStreamAsync(streamId, token: ct);
            events.Count(candidate => candidate.Data is OrderTicketed).ShouldBe(1);
        }
        firstOutbox.Published.OfType<OrderTicketedNotification>().Count().ShouldBe(1);
        _db.WebhookInbox.Single(candidate => candidate.Id == inbox.Id).ProcessedAt.ShouldBeNull();

        var retryOutbox = new RecordingMartenOutbox();
        await using (var retrySession = _store.LightweightSession())
        {
            await DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(inbox.Id),
                inboxStore,
                retrySession,
                NullMetrics,
                retryOutbox,
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                ct
            );
        }

        retryOutbox.Published.ShouldBeEmpty();
        _db.WebhookInbox.Single(candidate => candidate.Id == inbox.Id)
            .ProcessedAt.ShouldNotBeNull();
        await using var finalSession = _store.LightweightSession();
        var finalEvents = await finalSession.Events.FetchStreamAsync(streamId, token: ct);
        finalEvents.Count(candidate => candidate.Data is OrderTicketed).ShouldBe(1);
    }

    private sealed class FailingAcknowledgement(IWebhookInboxStore inner) : IWebhookInboxStore
    {
        public Task<WebhookInboxEntry?> FindAsync(Guid inboxId, CancellationToken ct) =>
            inner.FindAsync(inboxId, ct);

        public Task<Guid> FindAggregateIdByProviderOrderIdAsync(
            string providerOrderId,
            CancellationToken ct
        ) => inner.FindAggregateIdByProviderOrderIdAsync(providerOrderId, ct);

        public Task MarkProcessedAsync(
            Guid inboxId,
            DateTimeOffset processedAt,
            CancellationToken ct
        ) => throw new InjectedAcknowledgementFailureException();
    }

    private sealed class InjectedAcknowledgementFailureException : Exception
    {
        public InjectedAcknowledgementFailureException() { }

        public InjectedAcknowledgementFailureException(string message)
            : base(message) { }

        public InjectedAcknowledgementFailureException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}

file sealed class NullFlightsMetrics : IFlightsMetrics
{
    public int AirlineInitiatedChangeCount { get; private set; }

    public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

    public void RecordSearchError(string provider) { }

    public void RecordPaymentOutcome(bool success) { }

    public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

    public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

    public void RecordWebhookReceived(string eventType) { }

    public void RecordWebhookProcessingLag(double ms, string eventType) { }

    public void RecordAirlineInitiatedChange() => AirlineInitiatedChangeCount++;

    public void RecordPaymentDuration(double ms, string outcome) { }

    public void RecordNlSearchDuration(double ms) { }

    public void RecordSearchPartialFill(bool partial) { }

    public void RecordOfferShown() { }

    public void RecordOrderBooked() { }
}
