using System.Text.Json.Serialization;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.ValueObjects.Offer;

[JsonPolymorphic]
[JsonDerivedType(typeof(BookableOffer), "bookable")]
[JsonDerivedType(typeof(DeeplinkOffer), "deeplink")]
public abstract record Offer(
    OfferId Id,
    Itinerary Itinerary,
    Money TotalAmount,
    ProviderId Provider,
    DateTimeOffset FetchedAt
)
{
    /// <summary>
    /// Returns a copy of this offer with a new <see cref="TotalAmount"/> (used for FX normalisation).
    /// </summary>
    public abstract Offer WithAmount(Money amount);
}
