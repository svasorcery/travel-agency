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
        // For one-way trips: key = primary segment of the single slice.
        // For round-trips: composite key from the primary segment of EACH slice so that
        // two round-trips sharing the outbound but differing on the inbound are NOT merged.
        var sliceKeys = o.Itinerary.Slices.Select(s =>
        {
            var seg = s.Segments[0];
            var date = seg.DepartAt.UtcDateTime.Date.ToString("yyyy-MM-dd");
            return $"{seg.CarrierCode}|{seg.FlightNumber}|{date}";
        });
        return string.Join("//", sliceKeys);
    }
}
