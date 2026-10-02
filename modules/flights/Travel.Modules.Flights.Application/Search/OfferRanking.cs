using System.Globalization;
using System.Text.Json;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public enum RankingPriceState
{
    Native,
    Converted,
    FxUnavailable,
}

public sealed record RankingCandidate(Offer Offer, Money SourcePrice, RankingPriceState PriceState);

public sealed record OfferRankingEntry(
    Guid OfferId,
    string Currency,
    int Rank,
    Money SourcePrice,
    RankingPriceState PriceState,
    long? DurationSeconds,
    int? Transfers,
    IReadOnlyList<string> Limitations
);

public sealed record SearchRanking(
    string Policy,
    string RequestedCurrency,
    IReadOnlyList<OfferRankingEntry> Entries
);

public sealed record RankedOffers(IReadOnlyList<Offer> Offers, SearchRanking Ranking);

internal static class OfferRankingFacts
{
    public static long? Duration(Offer offer) =>
        offer is BookableOffer ? (long)offer.Itinerary.TotalDuration.Value.TotalSeconds : null;

    public static int? Transfers(Offer offer) =>
        offer is BookableOffer ? offer.Itinerary.Slices.Sum(s => s.Segments.Count - 1) : null;

    // JSON delimits strings safely. Explicit fields exclude generated IDs/fetch time.
    public static string Key(RankingCandidate candidate)
    {
        var offer = candidate.Offer;
        var bookable = offer as BookableOffer;
        var partner = offer as DeeplinkOffer;
        return JsonSerializer.Serialize(
            new
            {
                Provider = offer.Provider.Value,
                Kind = bookable is null ? "partner" : "bookable",
                Reference = bookable?.ProviderOfferRef ?? partner!.DeeplinkUrl.ToString(),
                Partner = partner?.PartnerName,
                Currency = offer.TotalAmount.Currency.Value,
                Amount = offer.TotalAmount.Amount.ToString("G29", CultureInfo.InvariantCulture),
                SourceCurrency = candidate.SourcePrice.Currency.Value,
                SourceAmount = candidate.SourcePrice.Amount.ToString(
                    "G29",
                    CultureInfo.InvariantCulture
                ),
                candidate.PriceState,
                Slices = offer.Itinerary.Slices.Select(s => new
                {
                    Origin = s.Origin.Value,
                    Destination = s.Destination.Value,
                    Duration = s.Duration.Value.Ticks,
                    Segments = s.Segments.Select(segment => new
                    {
                        Origin = segment.Origin.Value,
                        Destination = segment.Destination.Value,
                        Departure = segment.DepartAt.UtcTicks,
                        Arrival = segment.ArriveAt.UtcTicks,
                        segment.CarrierCode,
                        segment.FlightNumber,
                        Cabin = segment.Cabin.Code,
                    }),
                }),
                Fare = bookable?.FareConditions,
                Expiry = bookable?.ExpiresAt.UtcTicks,
            }
        );
    }
}
