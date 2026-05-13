namespace Travel.Modules.Flights.Infrastructure.Persistence.Entities;

public sealed class OrderReadModelEntity
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public Guid? UserId { get; set; }
    public string? ProviderOrderId { get; set; }
    public string Status { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = default!;
    public string ItineraryJson { get; set; } = default!;
    public string PassengerInfoJson { get; set; } = default!;
    public string[] TicketNumbers { get; set; } = Array.Empty<string>();
    public DateTimeOffset BookedAt { get; set; }
    public DateTimeOffset? TicketedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
}
