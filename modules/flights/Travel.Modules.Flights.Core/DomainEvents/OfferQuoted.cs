using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OfferQuoted(
    OfferId OfferId,
    Itinerary Itinerary,
    Money TotalAmount,
    DateTimeOffset ExpiresAt,
    string ProviderRef,
    DateTimeOffset QuotedAt
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => QuotedAt;
}
