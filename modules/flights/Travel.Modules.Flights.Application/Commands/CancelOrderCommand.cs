using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Commands;

public sealed record CancelOrderCommand(Guid AggregateId, Guid UserId);

public sealed record CancelledOrderResult(
    Guid AggregateId,
    string Status,
    OrderCommandSnapshot Snapshot
);

public sealed record OrderCommandSnapshot(
    Money TotalAmount,
    Itinerary Itinerary,
    IReadOnlyList<string> TicketNumbers,
    DateTimeOffset BookedAt,
    DateTimeOffset? TicketedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? RefundedAt
);
