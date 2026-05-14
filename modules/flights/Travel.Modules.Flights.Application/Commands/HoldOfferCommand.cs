using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Commands;

public sealed record HoldOfferCommand(Guid AggregateId, PassengerInfo Passenger);

public sealed record HeldOrderResult(
    Guid AggregateId,
    string ProviderOrderId,
    DateTimeOffset HeldUntil
);
