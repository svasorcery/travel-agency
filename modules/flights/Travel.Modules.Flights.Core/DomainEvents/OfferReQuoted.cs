using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OfferReQuoted(
    OfferId OfferId,
    Money OldAmount,
    Money NewAmount,
    DateTimeOffset ReQuotedAt,
    BookableOffer? RefreshedOffer = null
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => ReQuotedAt;
}
