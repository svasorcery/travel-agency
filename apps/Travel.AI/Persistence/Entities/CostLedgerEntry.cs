namespace Travel.AI.Persistence.Entities;

public sealed class CostLedgerEntry
{
    public Guid Id { get; set; }
    public string Feature { get; set; } = default!;
    public string Model { get; set; } = default!;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public Guid? UserId { get; set; }
    public string MessageIdentity { get; set; } = default!;
    public Guid CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
