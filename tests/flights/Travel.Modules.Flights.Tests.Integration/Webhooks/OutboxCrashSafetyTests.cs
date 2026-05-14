using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

/// <summary>
/// Crash-safety test for the transactional outbox. It exercises the persist-then-publish
/// gap from the other direction: a handler inserts the webhook-inbox row and publishes the
/// follow-up command, then throws BEFORE returning — i.e. before Wolverine commits the
/// DbContext transaction and flushes the outbox. Because the inbox INSERT and the outgoing
/// message share one transaction, the failure must roll BOTH back: no orphaned inbox row,
/// no orphaned outbox message. This is the guarantee that closes the
/// <c>SaveChangesAsync</c>-then-separate-<c>PublishAsync</c> crash window.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OutboxCrashSafetyTests : IClassFixture<WolverineOutboxFixture>
{
    private readonly WolverineOutboxFixture _fixture;

    public OutboxCrashSafetyTests(WolverineOutboxFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Webhook_inbox_and_outbox_message_commit_atomically()
    {
        var ct = TestContext.Current.CancellationToken;
        var inboxId = Guid.NewGuid();
        var eventId = "wh_crash_" + Guid.NewGuid().ToString("N");

        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Invoke the [Transactional] handler inline. It inserts the inbox row, publishes
        // ProcessDuffelWebhookCommand, then throws before returning. The handler's
        // transaction has not committed yet, so the throw must roll everything back.
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await bus.InvokeAsync(
                new EfOutboxProbeCommand(inboxId, eventId, ThrowAfterPublish: true),
                ct
            )
        );
        ex.Message.ShouldContain("simulated post-publish failure");

        // The inbox row was rolled back — the failed transaction left nothing behind.
        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var rowCount = await db.WebhookInbox.CountAsync(
            x => x.Source == "duffel" && x.EventId == eventId,
            ct
        );
        rowCount.ShouldBe(0);

        // The outgoing command was rolled back with the transaction — prove it at the storage
        // level rather than waiting an arbitrary delay. Because the handler threw before
        // Wolverine committed, the outbox row was never inserted. Querying the durable-message
        // store immediately after the exception therefore returns zero envelopes for this
        // message type. If the rollback had been incomplete an orphaned row would still be
        // present (the durability agent has not had a chance to process it yet).
        var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        var outgoing = await runtime.Storage.Admin.AllOutgoingAsync();
        var expectedMessageType = typeof(ProcessDuffelWebhookCommand).FullName!;
        outgoing
            .Where(e => e.MessageType == expectedMessageType)
            .ShouldBeEmpty(
                "A rolled-back transaction must leave no outbox row for the published command."
            );

        // Belt-and-suspenders: the probe handler must not have been invoked either, confirming
        // the message never escaped into the delivery pipeline.
        _fixture.Probe.WasHandled(inboxId).ShouldBeFalse();
    }
}
