using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.ValueObjects.Offer;

public sealed record BookableOffer(
    OfferId Id,
    Itinerary Itinerary,
    Money TotalAmount,
    ProviderId Provider,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    FareConditions FareConditions,
    string ProviderOfferRef
) : Offer(Id, Itinerary, TotalAmount, Provider, FetchedAt);
