namespace Travel.Modules.Flights.Application.Handlers.Webhooks;

/// <summary>
/// Thin abstraction over webhook-inbox persistence used by DuffelWebhookHandler.
/// Concrete implementation lives in Infrastructure (EF Core) to avoid a circular
/// project dependency (Infrastructure already references Application).
/// </summary>
public interface IWebhookInboxStore
{
    Task<WebhookInboxEntry?> FindAsync(Guid inboxId, CancellationToken ct);
    Task MarkProcessedAsync(Guid inboxId, DateTimeOffset processedAt, CancellationToken ct);
    Task<Guid> FindAggregateIdByProviderOrderIdAsync(string providerOrderId, CancellationToken ct);
    Task<Guid?> FindUserIdByAggregateIdAsync(Guid aggregateId, CancellationToken ct);
}

public sealed record WebhookInboxEntry(
    Guid Id,
    string EventType,
    string RawPayload,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt
);
