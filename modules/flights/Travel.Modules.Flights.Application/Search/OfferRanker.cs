using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public static class OfferRanker
{
    public const string Policy = "price-first-v1";

    public static RankedOffers Rank(
        IEnumerable<RankingCandidate> candidates,
        CurrencyCode requestedCurrency,
        int top = 200
    )
    {
        var ordered = candidates
            .Select(c => new
            {
                Candidate = c,
                Duration = OfferRankingFacts.Duration(c.Offer),
                Transfers = OfferRankingFacts.Transfers(c.Offer),
                Key = OfferRankingFacts.Key(c),
            })
            .OrderBy(c => c.Candidate.Offer.TotalAmount.Currency == requestedCurrency ? 0 : 1)
            .ThenBy(c => c.Candidate.Offer.TotalAmount.Currency.Value, StringComparer.Ordinal)
            .ThenBy(c => c.Candidate.Offer.TotalAmount.Amount)
            .ThenBy(c => c.Duration.HasValue ? 0 : 1)
            .ThenBy(c => c.Duration)
            .ThenBy(c => c.Transfers.HasValue ? 0 : 1)
            .ThenBy(c => c.Transfers)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ThenBy(c => c.Candidate.Offer.Id.Value)
            .Take(Math.Clamp(top, 0, 200))
            .ToArray();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = ordered
            .Select(item =>
            {
                var c = item.Candidate;
                var currency = c.Offer.TotalAmount.Currency.Value;
                ranks[currency] = ranks.GetValueOrDefault(currency) + 1;
                var limitations = new List<string>();
                if (c.Offer is DeeplinkOffer)
                    limitations.Add("partial-itinerary");
                if (c.PriceState == RankingPriceState.FxUnavailable)
                    limitations.Add("fx-unavailable");
                return new OfferRankingEntry(
                    c.Offer.Id.Value,
                    currency,
                    ranks[currency],
                    c.SourcePrice,
                    c.PriceState,
                    item.Duration,
                    item.Transfers,
                    limitations
                );
            })
            .ToArray();
        return new RankedOffers(
            ordered.Select(c => c.Candidate.Offer).ToArray(),
            new SearchRanking(Policy, requestedCurrency.Value, entries)
        );
    }

    public static bool IsValid(SearchRanking? ranking, IReadOnlyList<Offer>? offers)
    {
        if (
            ranking is null
            || offers is null
            || ranking.Policy != Policy
            || ranking.Entries is null
            || ranking.Entries.Count != offers.Count
            || offers.Count > 200
        )
            return false;
        var currency = CurrencyCode.Create(ranking.RequestedCurrency);
        if (currency.IsError || ranking.RequestedCurrency != currency.Value.Value)
            return false;
        var candidates = new List<RankingCandidate>();
        var ids = new HashSet<Guid>();
        for (var i = 0; i < offers.Count; i++)
        {
            var o = offers[i];
            var e = ranking.Entries[i];
            if (
                o is null
                || e is null
                || !ValidMoney(e.SourcePrice)
                || !ValidMoney(o.TotalAmount)
                || e.DurationSeconds < 0
                || e.Transfers < 0
                || e.Limitations is null
                || e.OfferId != o.Id.Value
                || !ids.Add(e.OfferId)
                || e.Currency != o.TotalAmount.Currency.Value
                || !Enum.IsDefined(e.PriceState)
            )
                return false;
            var native = e.SourcePrice.Currency == currency.Value;
            if (
                e.PriceState == RankingPriceState.Native
                && (!native || e.SourcePrice != o.TotalAmount)
            )
                return false;
            if (
                e.PriceState == RankingPriceState.Converted
                && (native || o.TotalAmount.Currency != currency.Value)
            )
                return false;
            if (
                e.PriceState == RankingPriceState.FxUnavailable
                && (native || e.SourcePrice != o.TotalAmount)
            )
                return false;
            candidates.Add(new RankingCandidate(o, e.SourcePrice, e.PriceState));
        }
        var expected = Rank(candidates, currency.Value).Ranking.Entries;
        return expected
            .Zip(ranking.Entries)
            .All(pair =>
                pair.First.OfferId == pair.Second.OfferId
                && pair.First.Rank == pair.Second.Rank
                && pair.First.DurationSeconds == pair.Second.DurationSeconds
                && pair.First.Transfers == pair.Second.Transfers
                && pair.First.Limitations.SequenceEqual(pair.Second.Limitations)
            );
    }

    // JSON restoration bypasses value-object factories. Validate both original and
    // displayed money before treating a restored snapshot as explanation evidence.
    private static bool ValidMoney(Money? money) =>
        money is not null
        && money.Amount >= 0
        && money.Currency is not null
        && !CurrencyCode.Create(money.Currency.Value).IsError;
}
