namespace Travel.Modules.Flights.Application.Queries;

public sealed record GetOrderQuery(Guid AggregateId, Guid UserId);

public sealed record ListOrdersQuery(Guid UserId, int Limit = 50, int Offset = 0);

/// <summary>
/// Read-model DTO returned to handlers. Application-owned shape — not the EF entity.
/// </summary>
public sealed record OrderView(
    Guid AggregateId,
    Guid? UserId,
    string? ProviderOrderId,
    string Status,
    decimal TotalAmount,
    string Currency,
    string ItineraryJson,
    string PassengerInfoJson,
    IReadOnlyList<string> TicketNumbers,
    DateTimeOffset BookedAt,
    DateTimeOffset? TicketedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? RefundedAt
);

public sealed record OrderListView(IReadOnlyList<OrderView> Items, int Limit, int Offset);

public interface IOrderReadModelQueries
{
    Task<OrderView?> GetAsync(Guid aggregateId, Guid userId, CancellationToken ct);
    Task<OrderListView> ListAsync(Guid userId, int limit, int offset, CancellationToken ct);
}
