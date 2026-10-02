namespace Travel.Modules.Flights.Application.Search;

public static class OfferDeduplicator
{
    public static IReadOnlyList<RankingCandidate> Dedup(IEnumerable<RankingCandidate> offers) =>
        offers
            .GroupBy(OfferRankingFacts.Key, StringComparer.Ordinal)
            .Select(g =>
                g.OrderByDescending(o => o.Offer.FetchedAt).ThenBy(o => o.Offer.Id.Value).First()
            )
            .ToList();
}
