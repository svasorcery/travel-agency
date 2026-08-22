using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Tracking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

/// <summary>
/// Primitive EF-enrolled Wolverine outbox characterization through
/// <see cref="EfWebhookEndpointProbeHandler"/>. These tests do not prove the production
/// ingestion path. <see cref="DuffelWebhookIngestionPortTests"/> resolves and exercises
/// the actual port for inbox/outbox atomicity.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DuffelWebhookEndpointOutboxTests : IClassFixture<WolverineOutboxFixture>
{
    private readonly WolverineOutboxFixture _fixture;

    public DuffelWebhookEndpointOutboxTests(WolverineOutboxFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Concurrent_duplicate_webhooks_both_return_200_and_publish_exactly_once()
    {
        var eventId = "wh_concurrent_" + Guid.NewGuid().ToString("N");
        var winnerInboxId = Guid.NewGuid();
        var loserInboxId = Guid.NewGuid();

        // Drive both probes through the host concurrently. Each probe calls
        // IDbContextOutbox<FlightsDbContext>.PublishAsync to buffer the outgoing
        // ProcessDuffelWebhookCommand, then SaveChangesAndFlushMessagesAsync to
        // commit the inbox INSERT and flush the buffered messages inside one
        // DbContext transaction. The loser's transaction faults on the
        // (source,event_id) unique index — the 23505 catch in the probe converts
        // that to a clean false return, and the buffered command is discarded with
        // the failed transaction.
        await using var s1 = _fixture.Host.Services.CreateAsyncScope();
        await using var s2 = _fixture.Host.Services.CreateAsyncScope();
        var bus1 = s1.ServiceProvider.GetRequiredService<IMessageBus>();
        var bus2 = s2.ServiceProvider.GetRequiredService<IMessageBus>();

        var session = await _fixture
            .Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .WaitForMessageToBeReceivedAt<ProcessDuffelWebhookCommand>(_fixture.Host)
            .ExecuteAndWaitAsync(
                (IMessageContext _) =>
                {
                    var t1 = Task.Run(() =>
                        bus1.InvokeAsync<bool>(
                            new EfWebhookEndpointProbeCommand(winnerInboxId, eventId)
                        )
                    );
                    var t2 = Task.Run(() =>
                        bus2.InvokeAsync<bool>(
                            new EfWebhookEndpointProbeCommand(loserInboxId, eventId)
                        )
                    );
                    return Task.WhenAll(t1, t2);
                }
            );

        // Exactly one row.
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var rows = await db
            .WebhookInbox.Where(x => x.Source == "duffel" && x.EventId == eventId)
            .ToListAsync(TestContext.Current.CancellationToken);
        rows.Count.ShouldBe(1);

        // Exactly one ProcessDuffelWebhookCommand reached its handler.
        _fixture.Probe.HandledCount(rows[0].Id).ShouldBe(1);

        var loserId = rows[0].Id == winnerInboxId ? loserInboxId : winnerInboxId;
        _fixture
            .Probe.HandledCount(loserId)
            .ShouldBe(0, "the rolled-back transaction must not deliver its publish.");
    }

    [Fact]
    public async Task Inbox_insert_and_command_publish_are_atomic_on_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        var inboxId = Guid.NewGuid();
        var eventId = "wh_rollback_" + Guid.NewGuid().ToString("N");

        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Probe throws after publish — same pattern as OutboxCrashSafetyTests.
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await bus.InvokeAsync<bool>(
                new EfWebhookEndpointProbeCommand(inboxId, eventId, ThrowAfterPublish: true),
                ct
            )
        );
        ex.Message.ShouldContain("simulated post-publish failure");

        // No inbox row.
        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var count = await db.WebhookInbox.CountAsync(
            x => x.Source == "duffel" && x.EventId == eventId,
            ct
        );
        count.ShouldBe(0);

        // No outbox row for the published command — the durability agent hasn't had a
        // chance to process anything, so a present row would imply the rollback was
        // incomplete. (Matches OutboxCrashSafetyTests storage-level assertion.)
        var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        var outgoing = await runtime.Storage.Admin.AllOutgoingAsync();
        var expectedMessageType = typeof(ProcessDuffelWebhookCommand).FullName!;
        outgoing
            .Where(e => e.MessageType == expectedMessageType)
            .ShouldBeEmpty(
                "a rolled-back transaction must leave no outbox row for the published command."
            );

        _fixture.Probe.WasHandled(inboxId).ShouldBeFalse();
    }
}
