namespace Travel.Modules.Flights.Application.Commands;

public sealed record ConfirmOrderCommand(Guid AggregateId, Guid UserId);

public sealed record ConfirmedOrderResult(Guid AggregateId, string Status, string PaymentRef);
