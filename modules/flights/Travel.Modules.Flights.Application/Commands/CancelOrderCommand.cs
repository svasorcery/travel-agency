namespace Travel.Modules.Flights.Application.Commands;

public sealed record CancelOrderCommand(Guid AggregateId, Guid UserId);

public sealed record CancelledOrderResult(Guid AggregateId, string Status);
