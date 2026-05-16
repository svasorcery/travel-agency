using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

/// <summary>
/// A new offer was quoted, starting a booking stream. FareConditions are captured
/// here so the subsequent hold can carry the exact terms shown to the user — see
/// <see cref="HoldOfferHandler"/>. The property is optional for forward
/// compatibility with any historical streams that predate the field.
/// </summary>
public sealed record OfferQuoted(
    OfferId OfferId,
    Itinerary Itinerary,
    Money TotalAmount,
    DateTimeOffset ExpiresAt,
    string ProviderRef,
    DateTimeOffset QuotedAt,
    FareConditions? FareConditions = null
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => QuotedAt;
}
