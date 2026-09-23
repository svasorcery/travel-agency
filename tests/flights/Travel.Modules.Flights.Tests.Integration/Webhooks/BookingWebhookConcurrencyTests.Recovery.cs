using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Webhooks;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

public sealed partial class BookingWebhookConcurrencyTests
{
    [Fact]
    public async Task Committed_ticket_recovers_failed_ProcessedAt_save_without_duplicate_events_or_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var faults = new ConvergenceFaults
        {
            Window = ConvergenceFaultWindow.BeforeWebhookAcknowledgement,
        };
        using var host = await WolverineOutboxFixture.StartConvergenceHostAsync(
            _pg.GetConnectionString(),
            faults,
            new ConvergenceExternalServices(),
            true,
            ct
        );
        try
        {
            var id = await SeedConfirmedStream("ord-recovery", Guid.NewGuid(), ct);
            var inboxId = await PrepareRealWebhookAsync(host, id, "ord-recovery", ct);
            faults.AggregateId = id;
            faults.InboxId = inboxId;
            faults.Armed = true;
            await using (var publishScope = host.Services.CreateAsyncScope())
                await publishScope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new ProcessDuffelWebhookCommand(inboxId));
            await ConvergenceFaults.WaitAsync(faults.AtFault.Task, ct);
            // Independent sessions prove the exact window: committed ticket, still-unprocessed EF inbox.
            await AssertTicketCountAsync(id, 1, ct);
            (await ReadInboxAsync(host, inboxId, ct)).ProcessedAt.ShouldBeNull();
            var original = faults.WebhookEnvelopes.ShouldHaveSingleItem();
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            (await runtime.Storage.Admin.AllIncomingAsync()).ShouldContain(x => x.Id == original);
            faults.ReleaseFault.TrySetResult();
            await ConvergenceFaults.WaitAsync(faults.AtRetry.Task, ct);
            (await ReadInboxAsync(host, inboxId, ct)).ProcessedAt.ShouldBeNull();
            faults.ReleaseRetry.TrySetResult();
            await WaitForRecoveryAsync(
                async () => (await ReadInboxAsync(host, inboxId, ct)).ProcessedAt != null,
                ct
            );
            await WaitForRecoveryAsync(
                async () =>
                    !(await runtime.Storage.Admin.AllIncomingAsync()).Any(x =>
                        x.Status != EnvelopeStatus.Handled
                    ),
                ct
            );
            faults.WebhookEnvelopes.ToArray().ShouldBe(new[] { original, original });
            faults.WebhookContexts.Distinct().Count().ShouldBe(2);
            faults.ReconcileEnvelopes.ShouldHaveSingleItem();
            faults.Notifications.Select(x => x.Id).Distinct().ShouldHaveSingleItem();
            faults.Notifications.ShouldAllBe(x => x.Version == 5);
            faults.InjectedFaults.ShouldBe(1);
            await AssertTicketCountAsync(id, 1, ct);
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
            var order = await EntityFrameworkQueryableExtensions.SingleAsync(
                db.Orders.AsNoTracking(),
                x => x.AggregateId == id,
                ct
            );
            order.ProjectedStreamVersion.ShouldBe(5);
            order.Status.ShouldBe("Ticketed");
            (
                await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(original)
            ).ShouldBeNull();
        }
        finally
        {
            faults.ReleaseAll();
            await host.StopAsync(ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_write_conflict_and_requested_cancellation_remain_distinct_without_acknowledgement(
        bool cancel
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var faults = new ConvergenceFaults();
        using var host = await WolverineOutboxFixture.StartConvergenceHostAsync(
            _pg.GetConnectionString(),
            faults,
            new ConvergenceExternalServices(),
            true,
            ct
        );
        try
        {
            var id = await SeedConfirmedStream("ord-boundary", Guid.NewGuid(), ct);
            var inboxId = await PrepareRealWebhookAsync(host, id, "ord-boundary", ct);
            using var requested = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
                session.Listeners.Add(
                    new BeforeWebhookCommit(async () =>
                    {
                        if (cancel)
                            requested.Cancel();
                        else
                        {
                            await using var winner = _store.LightweightSession();
                            var stream = await winner.Events.FetchForWriting<BookingAggregate>(
                                id,
                                ct
                            );
                            stream.AppendOne(
                                new OrderTicketed(
                                    new EquatableArray<string>(["TKT-WINNER"]),
                                    DateTimeOffset.UtcNow
                                )
                            );
                            await winner.SaveChangesAsync(ct);
                        }
                    })
                );
                var error = await Record.ExceptionAsync(() =>
                    DuffelWebhookHandler.Handle(
                        new ProcessDuffelWebhookCommand(inboxId),
                        scope.ServiceProvider.GetRequiredService<IWebhookInboxStore>(),
                        session,
                        NullFlightsMetricsImpl.Instance,
                        scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                        TimeProvider.System,
                        NullLogger<ProcessDuffelWebhookCommand>.Instance,
                        requested.Token
                    )
                );
                if (cancel)
                {
                    error.ShouldBeAssignableTo<OperationCanceledException>();
                    BookingStorageFailureClassifier
                        .Classify(error!, requested.Token)
                        .ShouldBeSameAs(error);
                }
                else
                    error.ShouldBeOfType<BookingWriteConflictException>();
            }
            (await ReadInboxAsync(host, inboxId, ct)).ProcessedAt.ShouldBeNull();
            await AssertTicketCountAsync(id, cancel ? 0 : 1, ct);
            var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            (await runtime.Storage.Admin.AllIncomingAsync()).ShouldBeEmpty();
            (await runtime.Storage.Admin.AllOutgoingAsync()).ShouldBeEmpty();
            // Fresh manual delivery models resumption, not a policy retry of requested cancellation.
            await using (var retryScope = host.Services.CreateAsyncScope())
                await retryScope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeAsync(new ProcessDuffelWebhookCommand(inboxId), ct);
            (await ReadInboxAsync(host, inboxId, ct)).ProcessedAt.ShouldNotBeNull();
            await AssertTicketCountAsync(id, 1, ct);
        }
        finally
        {
            faults.ReleaseAll();
            await host.StopAsync(ct);
        }
    }

    private async Task<Guid> PrepareRealWebhookAsync(
        IHost host,
        Guid id,
        string providerOrderId,
        CancellationToken ct
    )
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IOrderReadModelReconciler>()
            .ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var inboxId = Guid.NewGuid();
        db.WebhookInbox.Add(
            new WebhookInboxEntity
            {
                Id = inboxId,
                Source = "duffel",
                EventId = inboxId.ToString(),
                EventType = "order.created",
                RawPayload = BuildTicketPayload(providerOrderId, "TKT-RECOVERED"),
                Signature = "test",
                ReceivedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        return inboxId;
    }

    private static async Task<WebhookInboxEntity> ReadInboxAsync(
        IHost host,
        Guid id,
        CancellationToken ct
    )
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await EntityFrameworkQueryableExtensions.SingleAsync(
            scope
                .ServiceProvider.GetRequiredService<FlightsDbContext>()
                .WebhookInbox.AsNoTracking(),
            x => x.Id == id,
            ct
        );
    }

    private async Task AssertTicketCountAsync(Guid id, int count, CancellationToken ct)
    {
        await using var verify = _store.QuerySession();
        var events = await verify.Events.FetchStreamAsync(id, token: ct);
        events.Count.ShouldBe(4 + count);
        events.Count(x => x.Data is OrderTicketed).ShouldBe(count);
    }

    private static async Task WaitForRecoveryAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        while (!await condition())
            await Task.Delay(50, timeout.Token);
    }

    private sealed class BeforeWebhookCommit(Func<Task> action) : DocumentSessionListenerBase
    {
        public override async Task BeforeSaveChangesAsync(
            IDocumentSession session,
            CancellationToken token
        ) => await action();
    }
}
