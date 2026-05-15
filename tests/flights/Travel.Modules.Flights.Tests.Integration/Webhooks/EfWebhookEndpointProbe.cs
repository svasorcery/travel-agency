using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Wolverine.Attributes;
using Wolverine.EntityFrameworkCore;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

/// <summary>
/// Drives a probe that mirrors <c>DuffelWebhookEndpoint.Receive</c>'s transaction shape:
/// stage the inbox INSERT on the DbContext, publish the follow-up command via the
/// EF-enrolled outbox, then <c>SaveChangesAndFlushMessagesAsync</c> to commit both
/// atomically — with a 23505-catch that turns the concurrent unique-violation into a
/// clean dedup. Because the real endpoint is invoked through the ASP.NET pipeline, a
/// test that boots <see cref="Outbox.WolverineOutboxFixture"/> cannot drive the
/// endpoint directly; this handler stands in for it and is wired up to the same
/// Wolverine host so the outbox semantics under test are identical.
///
/// DRIFT HAZARD — keep <see cref="Handle"/> in sync with
/// <c>DuffelWebhookEndpoint.Receive</c>. If the endpoint's transaction shape changes
/// (e.g. ordering of <c>Add</c> / <c>outbox.PublishAsync</c> /
/// <c>SaveChangesAndFlushMessagesAsync</c>, or the 23505-handling path), update this
/// probe too.
/// </summary>
public sealed record EfWebhookEndpointProbeCommand(
    Guid InboxId,
    string EventId,
    bool ThrowAfterPublish = false
);

public static class EfWebhookEndpointProbeHandler
{
    [WolverineHandler]
    public static async Task<bool> Handle(
        EfWebhookEndpointProbeCommand cmd,
        FlightsDbContext db,
        IDbContextOutbox<FlightsDbContext> outbox,
        ILogger<EfWebhookEndpointProbeCommand> log,
        CancellationToken ct
    )
    {
        // Pre-check fast path (matches the endpoint).
        var existing = await db
            .WebhookInbox.AsNoTracking()
            .AnyAsync(x => x.Source == "duffel" && x.EventId == cmd.EventId, ct);
        if (existing)
            return false;

        var row = new WebhookInboxEntity
        {
            Id = cmd.InboxId,
            Source = "duffel",
            EventId = cmd.EventId,
            EventType = "order.created",
            RawPayload = """{"id":"evt","object":{"id":"ord"}}""",
            Signature = "t=0,v1=00",
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        db.WebhookInbox.Add(row);

        await outbox.PublishAsync(new ProcessDuffelWebhookCommand(cmd.InboxId));

        if (cmd.ThrowAfterPublish)
        {
            log.LogWarning("EfWebhookEndpointProbe: throwing after publish.");
            throw new InvalidOperationException(
                "EfWebhookEndpointProbe: simulated post-publish failure."
            );
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.Entry(row).State = EntityState.Detached;
            return false;
        }
        catch (PostgresException pgex) when (pgex.SqlState == "23505")
        {
            db.Entry(row).State = EntityState.Detached;
            return false;
        }

        return true;
    }
}
