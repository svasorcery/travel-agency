using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Webhooks;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

/// <summary>
/// EF Core-backed implementation of IWebhookInboxStore.
/// Lives in Infrastructure to avoid a circular project dependency with Application.
/// </summary>
public sealed class WebhookInboxStore(FlightsDbContext db, ILogger<WebhookInboxStore> log)
    : IWebhookInboxStore
{
    public async Task<WebhookInboxEntry?> FindAsync(Guid inboxId, CancellationToken ct)
    {
        var entity = await db.WebhookInbox.FirstOrDefaultAsync(x => x.Id == inboxId, ct);
        if (entity is null)
            return null;

        return new WebhookInboxEntry(
            entity.Id,
            entity.EventType,
            entity.RawPayload,
            entity.ReceivedAt,
            entity.ProcessedAt
        );
    }

    public async Task MarkProcessedAsync(
        Guid inboxId,
        DateTimeOffset processedAt,
        CancellationToken ct
    )
    {
        var entity = await db.WebhookInbox.FirstOrDefaultAsync(x => x.Id == inboxId, ct);
        if (entity is null)
        {
            // Soft no-op contract is preserved (no throw), but we surface the silent
            // failure via a warning so it is observable instead of disappearing.
            log.LogWarning(
                "Webhook inbox row {InboxId} not found when marking processed.",
                inboxId
            );
            return;
        }

        entity.ProcessedAt = processedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> FindAggregateIdByProviderOrderIdAsync(
        string providerOrderId,
        CancellationToken ct
    ) =>
        await db
            .Orders.Where(o => o.ProviderOrderId == providerOrderId)
            .Select(o => o.AggregateId)
            .FirstOrDefaultAsync(ct);

    public async Task<Guid?> FindUserIdByAggregateIdAsync(Guid aggregateId, CancellationToken ct) =>
        await db
            .Orders.Where(o => o.AggregateId == aggregateId)
            .Select(o => o.UserId)
            .FirstOrDefaultAsync(ct);
}
