using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Marten;
using Wolverine.Tracking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class BookingCommitOutboxTests : IClassFixture<WolverineOutboxFixture>
{
    private readonly WolverineOutboxFixture _fixture;

    public BookingCommitOutboxTests(WolverineOutboxFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Event_reconcile_and_sibling_notification_commit_and_deliver_together()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();

        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            var innerOutbox = scope.ServiceProvider.GetRequiredService<IMartenOutbox>();
            var outbox = new EnrollmentAssertingMartenOutbox(innerOutbox);
            session.Events.StartStream<BookingAggregate>(streamId, BuildOfferQuoted());

            await _fixture
                .Host.TrackActivity()
                .Timeout(TimeSpan.FromSeconds(30))
                .ExecuteAndWaitAsync(_ =>
                    session.SaveBookingWithReconcileAsync(
                        outbox,
                        streamId,
                        [new OutboxProbeMessage(siblingId)],
                        ct
                    )
                );

            outbox.EnrolledBeforeEveryPublish.ShouldBeTrue();
        }

        _fixture.Probe.WasHandled(streamId).ShouldBeTrue();
        _fixture.Probe.WasHandled(siblingId).ShouldBeTrue();

        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verify = store.LightweightSession();
        var aggregate = await verify.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(BookingStatus.OfferQuoted);
    }

    [Fact]
    public async Task Exception_before_save_commits_neither_event_nor_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();

        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            var innerOutbox = scope.ServiceProvider.GetRequiredService<IMartenOutbox>();
            var outbox = new EnrollmentAssertingMartenOutbox(
                innerOutbox,
                throwBeforeFirstPublish: true
            );
            session.Events.StartStream<BookingAggregate>(streamId, BuildOfferQuoted());

            await Should.ThrowAsync<InjectedPreSaveException>(() =>
                session.SaveBookingWithReconcileAsync(
                    outbox,
                    streamId,
                    [new OutboxProbeMessage(siblingId)],
                    ct
                )
            );
            outbox.EnrolledBeforeEveryPublish.ShouldBeTrue();
        }

        _fixture.Probe.WasHandled(streamId).ShouldBeFalse();
        _fixture.Probe.WasHandled(siblingId).ShouldBeFalse();
        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verify = store.LightweightSession();
        (await verify.Events.FetchStreamStateAsync(streamId, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Stale_expected_version_throws_classified_conflict_and_rolls_back_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var streamId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var quoted = BuildOfferQuoted();

        await using (var seedScope = _fixture.Host.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<IDocumentSession>();
            seed.Events.StartStream<BookingAggregate>(streamId, quoted);
            await seed.SaveChangesAsync(ct);
        }

        await using var winnerScope = _fixture.Host.Services.CreateAsyncScope();
        await using var loserScope = _fixture.Host.Services.CreateAsyncScope();
        var winner = winnerScope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var loser = loserScope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var winnerStream = await winner.Events.FetchForWriting<BookingAggregate>(streamId, ct);
        var loserStream = await loser.Events.FetchForWriting<BookingAggregate>(streamId, ct);
        winnerStream.AppendOne(BuildRequote(quoted, 5500m));
        loserStream.AppendOne(BuildRequote(quoted, 5600m));
        await winner.SaveChangesAsync(ct);

        var outbox = loserScope.ServiceProvider.GetRequiredService<IMartenOutbox>();
        var exception = await Should.ThrowAsync<BookingWriteConflictException>(() =>
            loser.SaveBookingWithReconcileAsync(
                outbox,
                streamId,
                [new OutboxProbeMessage(siblingId)],
                ct
            )
        );

        exception.AggregateId.ShouldBe(streamId);
        exception.InnerException.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
        _fixture.Probe.WasHandled(streamId).ShouldBeFalse();
        _fixture.Probe.WasHandled(siblingId).ShouldBeFalse();

        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verify = store.LightweightSession();
        var events = await verify.Events.FetchStreamAsync(streamId, token: ct);
        events.Count.ShouldBe(2);
        ((OfferReQuoted)events[^1].Data).NewAmount.Amount.ShouldBe(5500m);
    }

    private static OfferQuoted BuildOfferQuoted()
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
        var itinerary = Itinerary.Create([Slice.Create([segment]).Value]).Value;
        return new OfferQuoted(
            OfferId.New(),
            itinerary,
            Money.Create(5420m, CurrencyCode.Create("RUB").Value).Value,
            DateTimeOffset.UtcNow.AddMinutes(20),
            "off_" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow
        );
    }

    private static OfferReQuoted BuildRequote(OfferQuoted quoted, decimal amount) =>
        new(
            quoted.OfferId,
            quoted.TotalAmount,
            Money.Create(amount, quoted.TotalAmount.Currency).Value,
            DateTimeOffset.UtcNow
        );

    private sealed class EnrollmentAssertingMartenOutbox(
        IMartenOutbox inner,
        bool throwBeforeFirstPublish = false
    ) : IMartenOutbox
    {
        private bool _enrolled;
        private int _publishCount;

        public bool EnrolledBeforeEveryPublish { get; private set; } = true;

        public string? TenantId
        {
            get => inner.TenantId;
            set => inner.TenantId = value;
        }

        public IDocumentSession? Session => inner.Session;

        public void Enroll(IDocumentSession session)
        {
            _enrolled = true;
            inner.Enroll(session);
        }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            EnrolledBeforeEveryPublish &= _enrolled;
            if (throwBeforeFirstPublish && Interlocked.Increment(ref _publishCount) == 1)
                throw new InjectedPreSaveException();

            return inner.PublishAsync(message, options);
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            inner.SendAsync(message, options);

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => inner.BroadcastToTopicAsync(topicName, message, options);

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            inner.EndpointFor(endpointName);

        public IDestinationEndpoint EndpointFor(Uri uri) => inner.EndpointFor(uri);

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => inner.InvokeAsync(message, cancellation, timeout);

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => inner.InvokeAsync(message, options, cancellation, timeout);

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => inner.InvokeAsync<T>(message, cancellation, timeout);

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => inner.InvokeAsync<T>(message, options, cancellation, timeout);

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => inner.InvokeForTenantAsync(tenantId, message, cancellation, timeout);

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => inner.InvokeForTenantAsync<T>(tenantId, message, cancellation, timeout);

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            inner.PreviewSubscriptions(message);

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => inner.PreviewSubscriptions(message, options);
    }

    private sealed class InjectedPreSaveException : Exception
    {
        public InjectedPreSaveException() { }

        public InjectedPreSaveException(string message)
            : base(message) { }

        public InjectedPreSaveException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}

public static class ReconcileOrderReadModelProbeHandler
{
    [WolverineHandler]
    public static void Handle(ReconcileOrderReadModel message, OutboxProbeRecorder recorder) =>
        recorder.Record(message.AggregateId);
}
