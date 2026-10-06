using System.Text.Json;
using ErrorOr;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Handlers.Webhooks;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Travel.Shared.Abstractions;
using Travel.Tests.Fixtures;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Runtime;
using Wolverine.Tracking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class BookingProjectionOutboxTests(WolverineOutboxFixture fixture)
    : IClassFixture<WolverineOutboxFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("quote", false)]
    [InlineData("quote", true)]
    [InlineData("requote", false)]
    [InlineData("requote", true)]
    [InlineData("hold", false)]
    [InlineData("hold", true)]
    [InlineData("confirm", false)]
    [InlineData("confirm", true)]
    [InlineData("confirm-completion", true)]
    [InlineData("capture-failure", false)]
    [InlineData("capture-failure", true)]
    [InlineData("provider-failure", false)]
    [InlineData("provider-failure", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("ticket", false)]
    [InlineData("ticket", true)]
    [InlineData("refund", false)]
    [InlineData("refund", true)]
    public async Task Every_commit_shape_has_atomic_reconcile_and_exact_sibling_version(
        string shape,
        bool rollback
    )
    {
        var completionRollback = shape == "confirm-completion" && rollback;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var before = shape switch
        {
            "quote" => 0,
            "requote" or "hold" => 1,
            "ticket" or "refund" => 4,
            "confirm" or "confirm-completion" or "capture-failure" or "provider-failure" => 3,
            _ => 2,
        };
        var provider = new Provider(shape);
        var payments = new Payments(shape);
        var store = fixture.Host.Services.GetRequiredService<IDocumentStore>();
        if (before > 0)
        {
            await using var seed = store.LightweightSession();
            var quote = new OfferQuoted(
                provider.Offer.Id,
                provider.Offer.Itinerary,
                provider.Offer.TotalAmount,
                provider.Offer.ExpiresAt,
                provider.Offer.ProviderOfferRef,
                DateTimeOffset.UtcNow,
                QuoteBinding: TestPii.Binding()
            );
            seed.Events.StartStream<BookingAggregate>(id, quote);
            if (before >= 2)
                seed.Events.Append(
                    id,
                    BookingReconcilerFixture.Held(owner) with
                    {
                        HeldUntil = DateTimeOffset.UtcNow.AddHours(1),
                    }
                );
            if (
                shape
                is "confirm"
                    or "confirm-completion"
                    or "capture-failure"
                    or "provider-failure"
            )
                seed.Events.Append(
                    id,
                    new BookingMutationCoordinationEnabled(DateTimeOffset.UtcNow)
                );
            if (before >= 4)
                seed.Events.Append(
                    id,
                    new PaymentAuthorized(
                        PaymentRef.New(),
                        provider.Offer.TotalAmount,
                        DateTimeOffset.UtcNow
                    ),
                    new OrderConfirmed("ord-test", PaymentRef.New(), DateTimeOffset.UtcNow)
                );
            await seed.SaveChangesAsync(Ct);
        }
        var inbox = new Inbox(id, shape);
        var sibling = Guid.NewGuid();
        List<object> published;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            var outbox = new CommitOutbox(
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                sibling,
                rollback,
                rollbackAtCompletion: completionRollback
            );
            async Task Act()
            {
                switch (shape)
                {
                    case "quote":
                    case "requote":
                        var quote = await QuoteOfferHandler.Handle(
                            new QuoteOfferCommand(
                                "off-test",
                                ProviderId.Duffel,
                                shape == "quote" ? null : id
                            ),
                            [provider],
                            session,
                            outbox,
                            NullFlightsMetricsImpl.Instance,
                            TimeProvider.System,
                            NullLogger<QuoteOfferCommand>.Instance,
                            Ct
                        );
                        quote.IsError.ShouldBeFalse();
                        id = quote.Value.AggregateId;
                        break;
                    case "hold":
                        (
                            await HoldOfferHandler.Handle(
                                TestPii.HoldCommand(
                                    id,
                                    owner,
                                    BookingReconcilerFixture.Held(owner).Passenger,
                                    (
                                        await session.Events.AggregateStreamAsync<BookingAggregate>(
                                            id,
                                            token: Ct
                                        )
                                    )!.QuoteBinding!
                                ),
                                [provider],
                                session,
                                outbox,
                                NullFlightsMetricsImpl.Instance,
                                TimeProvider.System,
                                NullLogger<HoldOfferCommand>.Instance,
                                TestPii.PartyProtector,
                                Ct
                            )
                        ).IsError.ShouldBeFalse();
                        break;
                    case "confirm-completion":
                    case "confirm":
                    case "capture-failure":
                    case "provider-failure":
                        var result = await ConfirmOrderHandler.Handle(
                            new ConfirmOrderCommand(id, owner),
                            session,
                            [provider],
                            payments,
                            NullFlightsMetricsImpl.Instance,
                            outbox,
                            TimeProvider.System,
                            NullLogger<ConfirmOrderCommand>.Instance,
                            new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
                            Ct
                        );
                        result.IsError.ShouldBe(shape is not ("confirm" or "confirm-completion"));
                        break;
                    case "cancel":
                        var legacy = await CancelOrderHandler.Handle(
                            new CancelOrderCommand(id, owner),
                            session,
                            [provider],
                            NullFlightsMetricsImpl.Instance,
                            outbox,
                            TimeProvider.System,
                            NullLogger<CancelOrderCommand>.Instance,
                            Ct
                        );
                        legacy.IsError.ShouldBeTrue();
                        legacy.FirstError.Code.ShouldBe("Flights.CancellationTermsRequired");
                        break;
                    default:
                        await DuffelWebhookHandler.Handle(
                            new ProcessDuffelWebhookCommand(inbox.Entry.Id),
                            inbox,
                            session,
                            NullFlightsMetricsImpl.Instance,
                            outbox,
                            TimeProvider.System,
                            NullLogger<ProcessDuffelWebhookCommand>.Instance,
                            TestPii.WebhookReader,
                            Ct
                        );
                        break;
                }
            }
            if (shape == "cancel")
                await Act();
            else if (completionRollback)
                await fixture
                    .Host.TrackActivity()
                    .Timeout(TimeSpan.FromSeconds(30))
                    .ExecuteAndWaitAsync(_ => Should.ThrowAsync<InvalidOperationException>(Act));
            else if (rollback)
                await Should.ThrowAsync<InvalidOperationException>(Act);
            else
                await fixture
                    .Host.TrackActivity()
                    .Timeout(TimeSpan.FromSeconds(30))
                    .ExecuteAndWaitAsync(_ => Act());
            published = outbox.Published;
            if (shape == "quote" && rollback)
                id = published.OfType<ReconcileOrderReadModel>().Single().AggregateId;
        }
        await using var verify = store.QuerySession();
        var events = await verify.Events.FetchStreamAsync(id, token: Ct);
        var noCommit = shape == "cancel";
        var appended = shape switch
        {
            "cancel" => 0,
            "confirm" or "confirm-completion" => 7,
            "hold" => 2,
            "capture-failure" => 4,
            "provider-failure" => 5,
            _ => 1,
        };
        events.Count.ShouldBe(
            completionRollback ? before + 4
            : rollback ? before
            : before + appended
        );
        var expectedCommits =
            completionRollback ? 5
            : rollback ? (noCommit ? 0 : 1)
            : shape switch
            {
                "cancel" => 0,
                "confirm" or "provider-failure" => 5,
                "capture-failure" => 4,
                _ => 1,
            };
        published.OfType<ReconcileOrderReadModel>().Count().ShouldBe(expectedCommits);
        if (shape is "capture-failure" or "provider-failure" or "cancel")
        {
            var aggregate = await verify.Events.AggregateStreamAsync<BookingAggregate>(
                id,
                token: Ct
            );
            aggregate!.Status.ShouldBe(BookingStatus.Held);
            events
                .Any(e => e.Data is PaymentAuthorized or OrderConfirmed or OrderCancelled)
                .ShouldBeFalse();
            if (!rollback && shape != "cancel")
                aggregate.HasConfirmationBarrier.ShouldBeTrue();
        }
        var required = published
            .Select(x =>
                x switch
                {
                    OrderConfirmedNotification n => n.RequiredStreamVersion,
                    OrderCancelledNotification n => n.RequiredStreamVersion,
                    OrderTicketedNotification n => n.RequiredStreamVersion,
                    _ => null,
                }
            )
            .Where(x => x is not null)
            .ToArray();
        if (shape == "ticket" || shape == "confirm" && !rollback || completionRollback)
            required.ShouldBe(new long?[] { before + appended });
        else
            required.ShouldBeEmpty();
        fixture.Probe.WasHandled(id).ShouldBe(completionRollback || !rollback && !noCommit);
        fixture.Probe.WasHandled(sibling).ShouldBe(!rollback && required.Length != 0);
        inbox.Processed.ShouldBe(!rollback && shape is "ticket" or "refund");
        if (rollback)
        {
            var runtime = fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
            (await runtime.Storage.Admin.AllOutgoingAsync()).ShouldNotContain(x =>
                x.CorrelationId == sibling.ToString()
                && (!completionRollback || x.MessageType == typeof(OutboxProbeMessage).FullName)
            );
            (await runtime.Storage.Admin.AllIncomingAsync()).ShouldNotContain(x =>
                x.CorrelationId == sibling.ToString()
                && (!completionRollback || x.MessageType == typeof(OutboxProbeMessage).FullName)
            );
        }
        if (rollback && shape == "confirm")
        {
            provider.ConfirmCalls.ShouldBe(0);
            payments.Authorizations.ShouldBe(0);
            payments.Captures.ShouldBe(0);
        }
        if (completionRollback)
        {
            provider.ConfirmCalls.ShouldBe(1);
            payments.Authorizations.ShouldBe(1);
            payments.Captures.ShouldBe(1);
            events.Any(e => e.Data is PaymentAuthorized or OrderConfirmed).ShouldBeFalse();
            var pending = await verify.Events.AggregateStreamAsync<BookingAggregate>(id, token: Ct);
            pending!.HasConfirmationBarrier.ShouldBeTrue();
        }

        await using var db = new FlightsDbContext(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options
        );
        (
            await EntityFrameworkQueryableExtensions.AnyAsync(
                db.Orders,
                x => x.AggregateId == id,
                Ct
            )
        ).ShouldBeFalse("commands must not synchronously write EF");
    }

    [Fact]
    public async Task Losing_real_ticket_handler_leaves_no_sibling_or_reconcile_envelope()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var store = fixture.Host.Services.GetRequiredService<IDocumentStore>();
        var offer = new Provider("ticket").Offer;
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<BookingAggregate>(
                id,
                new OfferQuoted(
                    offer.Id,
                    offer.Itinerary,
                    offer.TotalAmount,
                    offer.ExpiresAt,
                    "off-test",
                    DateTimeOffset.UtcNow
                ),
                BookingReconcilerFixture.Held(owner),
                new PaymentAuthorized(PaymentRef.New(), offer.TotalAmount, DateTimeOffset.UtcNow),
                new OrderConfirmed("ord-test", PaymentRef.New(), DateTimeOffset.UtcNow)
            );
            await seed.SaveChangesAsync(Ct);
        }
        var inbox = new Inbox(id, "ticket");
        var losingSibling = Guid.NewGuid();
        async Task CommitWinner()
        {
            await using var winner = store.LightweightSession();
            var stream = await winner.Events.FetchForWriting<BookingAggregate>(id, Ct);
            stream.AppendOne(
                new OrderTicketed(new EquatableArray<string>(["TKT-WINNER"]), DateTimeOffset.UtcNow)
            );
            await winner.SaveChangesAsync(Ct);
        }
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            var outbox = new CommitOutbox(
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                losingSibling,
                false,
                CommitWinner
            );
            await Should.ThrowAsync<BookingWriteConflictException>(() =>
                DuffelWebhookHandler.Handle(
                    new ProcessDuffelWebhookCommand(inbox.Entry.Id),
                    inbox,
                    session,
                    NullFlightsMetricsImpl.Instance,
                    outbox,
                    TimeProvider.System,
                    NullLogger<ProcessDuffelWebhookCommand>.Instance,
                    TestPii.WebhookReader,
                    Ct
                )
            );
            outbox
                .Published.OfType<OrderTicketedNotification>()
                .ShouldHaveSingleItem()
                .RequiredStreamVersion.ShouldBe(5);
        }
        inbox.Processed.ShouldBeFalse();
        fixture.Probe.WasHandled(id).ShouldBeFalse();
        fixture.Probe.WasHandled(losingSibling).ShouldBeFalse();
        var runtime = fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        (await runtime.Storage.Admin.AllOutgoingAsync()).ShouldNotContain(x =>
            x.CorrelationId == losingSibling.ToString()
        );
        (await runtime.Storage.Admin.AllIncomingAsync()).ShouldNotContain(x =>
            x.CorrelationId == losingSibling.ToString()
        );
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var outbox = new CommitOutbox(
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                losingSibling,
                false
            );
            await DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(inbox.Entry.Id),
                inbox,
                scope.ServiceProvider.GetRequiredService<IDocumentSession>(),
                NullFlightsMetricsImpl.Instance,
                outbox,
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                TestPii.WebhookReader,
                Ct
            );
            outbox.Published.ShouldBeEmpty();
        }
        inbox.Processed.ShouldBeTrue();
        await using var verify = store.QuerySession();
        var events = await verify.Events.FetchStreamAsync(id, token: Ct);
        events.Count.ShouldBe(5);
        ((OrderTicketed)events[^1].Data).TicketNumbers.ShouldBe(new[] { "TKT-WINNER" });
    }

    private sealed class Provider(string shape) : IFlightBookingProvider
    {
        public ProviderId Id => ProviderId.Duffel;
        public int ConfirmCalls { get; private set; }
        public BookableOffer Offer { get; } =
            new(
                OfferId.New(),
                Itinerary
                    .Create([
                        Slice
                            .Create([
                                Segment
                                    .Create(
                                        IataCode.Create("LED").Value,
                                        IataCode.Create("DME").Value,
                                        DateTimeOffset.UtcNow.AddDays(1),
                                        DateTimeOffset.UtcNow.AddDays(1).AddHours(2),
                                        "SU",
                                        "100",
                                        CabinClass.Economy
                                    )
                                    .Value,
                            ])
                            .Value,
                    ])
                    .Value,
                BookingReconcilerFixture.Amount,
                ProviderId.Duffel,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                new FareConditions(false, false, null, null),
                "off-test",
                TestPii.Binding().Party
            );

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<BookableOffer>>(Offer);

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<HeldOrder>>(
                new HeldOrder("ord-test", DateTimeOffset.UtcNow.AddHours(1))
            );

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

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            CancellationToken ct
        )
        {
            ConfirmCalls++;
            return Task.FromResult<ErrorOr<ConfirmedOrder>>(
                shape == "provider-failure"
                    ? Error.Failure("Test.Provider")
                    : new ConfirmedOrder(
                        providerOrderId,
                        DateTimeOffset.UtcNow,
                        SupplierPaymentEvidence
                            .Create("pay_fictional", expectedTotal, SupplierPaymentKind.Balance)
                            .Value
                    )
            );
        }

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class Payments(string shape) : IPaymentGateway
    {
        public int Authorizations { get; private set; }
        public int Captures { get; private set; }

        public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
            Money amount,
            string idempotencyKey,
            CancellationToken ct
        )
        {
            Authorizations++;
            return Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());
        }

        public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct)
        {
            Captures++;
            return Task.FromResult<ErrorOr<Success>>(
                shape == "capture-failure" ? Error.Failure("Test.Capture") : Result.Success
            );
        }

        public Task<ErrorOr<RefundRef>> RefundAsync(
            PaymentRef payment,
            Money amount,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
    }

    private sealed class Inbox(Guid id, string shape) : IWebhookInboxStore
    {
        public WebhookInboxEntry Entry { get; } =
            new(
                Guid.NewGuid(),
                shape == "ticket" ? "order.created" : "order.airline_initiated_change.cancelled",
                JsonSerializer.Serialize(
                    new
                    {
                        @object = new
                        {
                            id = "ord-test",
                            documents = new[]
                            {
                                new { type = "ticket", unique_identifier = "TKT-1" },
                            },
                        },
                    }
                ),
                DateTimeOffset.UtcNow,
                null
            );
        public bool Processed { get; private set; }

        public Task<WebhookInboxEntry?> FindAsync(Guid inboxId, CancellationToken ct) =>
            Task.FromResult<WebhookInboxEntry?>(Entry);

        public Task MarkProcessedAsync(
            Guid inboxId,
            DateTimeOffset processedAt,
            CancellationToken ct
        )
        {
            Processed = true;
            return Task.CompletedTask;
        }

        public Task<Guid> FindAggregateIdByProviderOrderIdAsync(
            string providerOrderId,
            CancellationToken ct
        ) => Task.FromResult(id);
    }

    private sealed class CommitOutbox(
        IMartenOutbox suppliedInner,
        Guid sibling,
        bool rollback,
        Func<Task>? beforeReconcile = null,
        bool rollbackAtCompletion = false
    ) : IMartenOutbox
    {
        private readonly IMartenOutbox inner = suppliedInner.ForBookingCommits();
        public List<object> Published { get; } = [];
        public string? TenantId
        {
            get => inner.TenantId;
            set => inner.TenantId = value;
        }

        public IDocumentSession? Session => inner.Session;

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => inner.StreamAsync<TResponse>(message, cancellation);

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => inner.StreamAsync<TResponse>(message, options, cancellation);

        public void Enroll(IDocumentSession session) => inner.Enroll(session);

        public async ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            Published.Add(message!);
            options ??= new DeliveryOptions();
            options.CorrelationId = sibling.ToString();
            if (message is ReconcileOrderReadModel && beforeReconcile is not null)
                await beforeReconcile();
            if (
                message is ReconcileOrderReadModel
                && rollback
                && (!rollbackAtCompletion || Published.OfType<OrderConfirmedNotification>().Any())
            )
                throw new InvalidOperationException("Injected failure before commit");
            // Preserve the real Marten outbox transaction while routing sibling delivery
            // to the fixture's probe; exact production envelope fields are asserted above.
            if (
                message
                is ReconcileOrderReadModel
                    or Travel.Modules.Flights.Application.Cancellation.ConfirmationBarrierDeadline
            )
                await inner.PublishAsync(message, options);
            else
                await inner.PublishAsync(new OutboxProbeMessage(sibling), options);
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
}
