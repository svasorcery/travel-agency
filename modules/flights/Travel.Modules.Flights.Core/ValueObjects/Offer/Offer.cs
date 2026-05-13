using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.ValueObjects.Offer;

public abstract record Offer(
    OfferId Id,
    Itinerary Itinerary,
    Money TotalAmount,
    ProviderId Provider,
    DateTimeOffset FetchedAt
);
