using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Commands;

public sealed record HoldOfferCommand(
    Guid AggregateId,
    Guid UserId,
    Guid QuoteRevision,
    int PassengerCount,
    ProtectedPassengerPartySnapshot ProtectedPassengerParty,
    Guid RequestId = default,
    string? RequestDigest = null,
    bool AcceptAncillaries = false
)
{
    public override string ToString() => nameof(HoldOfferCommand);
}

public sealed record HeldOrderResult(
    Guid AggregateId,
    string ProviderOrderId,
    DateTimeOffset HeldUntil
);
