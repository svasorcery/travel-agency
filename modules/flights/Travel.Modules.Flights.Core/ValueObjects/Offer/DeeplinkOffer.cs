using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.ValueObjects.Offer;

public sealed record DeeplinkOffer(
    OfferId Id,
    Itinerary Itinerary,
    Money TotalAmount,
    ProviderId Provider,
    DateTimeOffset FetchedAt,
    Uri DeeplinkUrl,
    string PartnerName
) : Offer(Id, Itinerary, TotalAmount, Provider, FetchedAt)
{
    public override Offer WithAmount(Money amount) => this with { TotalAmount = amount };
}
