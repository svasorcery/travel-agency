using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Wolverine;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Tests.Integration.Outbox;

/// <summary>
/// Test-only command that drives <see cref="EfOutboxProbeHandler"/>. Carries everything
/// the handler needs to insert a <see cref="WebhookInboxEntity"/> and, optionally, throw
/// after the publish call so the atomicity (roll-back-everything) direction can be exercised.
/// </summary>
public sealed record EfOutboxProbeCommand(
    Guid InboxId,
    string EventId,
    bool ThrowAfterPublish = false
);

/// <summary>
/// A <c>[Transactional]</c> handler that takes <see cref="FlightsDbContext"/>: it inserts a
/// webhook-inbox row and publishes <see cref="ProcessDuffelWebhookCommand"/> through the
/// outbox. With Wolverine's EF Core integration the DbContext save and the outgoing message
/// commit in one transaction — exactly the production webhook-endpoint shape WS3 will adopt.
/// </summary>
public static class EfOutboxProbeHandler
{
    [Transactional]
    [WolverineHandler]
    public static async Task Handle(
        EfOutboxProbeCommand cmd,
        FlightsDbContext db,
        IMessageBus bus,
        ILogger<EfOutboxProbeCommand> log,
        CancellationToken ct
    )
    {
        db.WebhookInbox.Add(
            new WebhookInboxEntity
            {
                Id = cmd.InboxId,
                Source = "duffel",
                EventId = cmd.EventId,
                EventType = "order.created",
                RawPayload = """{"id":"evt_test","object":{"id":"ord_test"}}""",
                Signature = "sha256=test",
                ReceivedAt = DateTimeOffset.UtcNow,
            }
        );

        await bus.PublishAsync(new ProcessDuffelWebhookCommand(cmd.InboxId));

        if (cmd.ThrowAfterPublish)
        {
            // Thrown AFTER the publish call but BEFORE the handler returns. Wolverine has
            // not yet committed the DbContext transaction or flushed the outbox, so both
            // the inbox row and the outgoing message must roll back together.
            log.LogWarning("EfOutboxProbe: throwing after publish to assert atomic rollback.");
            throw new InvalidOperationException("EfOutboxProbe: simulated post-publish failure.");
        }
    }
}

/// <summary>
/// Recording handler for <see cref="ProcessDuffelWebhookCommand"/>. In this test host only
/// this handler is discovered (the Application assembly is not scanned by the fixture), so
/// it stands in for the real Duffel webhook handler and lets a test assert delivery.
/// </summary>
public static class ProcessDuffelWebhookProbeHandler
{
    [WolverineHandler]
    public static void Handle(ProcessDuffelWebhookCommand cmd, OutboxProbeRecorder recorder) =>
        recorder.Record(cmd.InboxId);
}
