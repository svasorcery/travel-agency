using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
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

        // The outgoing command was rolled back with the transaction — it never reaches its
        // handler. Give the (in this case empty) outbox a moment to prove nothing escaped.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        _fixture.Probe.WasHandled(inboxId).ShouldBeFalse();
    }
}
