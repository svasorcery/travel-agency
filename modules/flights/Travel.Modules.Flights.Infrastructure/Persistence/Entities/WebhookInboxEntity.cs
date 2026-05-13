namespace Travel.Modules.Flights.Infrastructure.Persistence.Entities;

public sealed class WebhookInboxEntity
{
    public Guid Id { get; set; }
    public string Source { get; set; } = default!;
    public string EventId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string RawPayload { get; set; } = default!;
    public string Signature { get; set; } = default!;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
