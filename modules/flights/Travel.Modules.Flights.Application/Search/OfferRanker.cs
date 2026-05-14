using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public static class OfferRanker
{
    public static IReadOnlyList<Offer> Rank(IEnumerable<Offer> offers, int top = 200) =>
        offers
            .OrderBy(o => o.TotalAmount.Amount)
            .ThenBy(o => o.Itinerary.TotalDuration.Value)
            .Take(top)
            .ToList();
}
