using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public static class OfferDeduplicator
{
    public static IReadOnlyList<Offer> Dedup(IEnumerable<Offer> offers) =>
        offers
            .GroupBy(KeyOf)
            .Select(g =>
                g.OrderBy(o => o.TotalAmount.Amount).ThenBy(o => o is DeeplinkOffer ? 1 : 0).First()
            )
            .ToList();

    private static string KeyOf(Offer o)
    {
        var primary = o.Itinerary.Slices[0].Segments[0];
        var dateKey = primary.DepartAt.UtcDateTime.Date.ToString("yyyy-MM-dd");
        return $"{primary.CarrierCode}|{primary.FlightNumber}|{dateKey}";
    }
}
