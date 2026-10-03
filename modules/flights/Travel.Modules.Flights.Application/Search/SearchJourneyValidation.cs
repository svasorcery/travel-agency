using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

/// <summary>Fresh search evidence only; historical order/event reads keep their stored facts.</summary>
public static class SearchJourneyValidation
{
    public static bool Matches(Offer offer, SearchCriteria criteria)
    {
        if (!IsValidOffer(offer) || !SearchPartyValidation.Matches(offer, criteria))
            return false;
        // Partner data is a limited summary; it cannot prove the complete requested journey.
        if (offer is DeeplinkOffer)
            return true;

        var itinerary = offer.Itinerary;
        if (
            itinerary.Slices.Count != criteria.Legs.Count
            || itinerary.JourneyKind != criteria.JourneyKind
        )
            return false;
        for (var i = 0; i < criteria.Legs.Count; i++)
        {
            var leg = criteria.Legs[i];
            var slice = itinerary.Slices[i];
            if (
                DateOnly.FromDateTime(slice.DepartAt.DateTime) != leg.DepartureDate
                || slice.Segments.Any(segment => segment.Cabin != criteria.CabinClass)
            )
                return false;
            if (
                criteria.RouteMode == SearchRouteMode.ExplicitAirportLegs
                && (slice.Origin != leg.Origin || slice.Destination != leg.Destination)
            )
                return false;
        }
        return true;
    }

    public static bool IsValid(
        SearchResult result,
        SearchCriteria criteria,
        IReadOnlyList<SkippedProvider> expectedSkips,
        IReadOnlySet<string> eligibleProviderIds
    )
    {
        if (
            result.Offers is null
            || result.PartialFailures is null
            || result.SkippedProviders is null
            || result.Ranking?.RequestedCurrency != criteria.Currency.Value
            || result.SkippedProviders.Count != expectedSkips.Count
            || !result
                .SkippedProviders.OrderBy(s => s?.Provider, StringComparer.Ordinal)
                .ThenBy(s => s?.ReasonCode, StringComparer.Ordinal)
                .SequenceEqual(
                    expectedSkips
                        .OrderBy(s => s.Provider, StringComparer.Ordinal)
                        .ThenBy(s => s.ReasonCode, StringComparer.Ordinal)
                )
            || !SearchPartyValidation.IsValid(result, criteria)
            || !result.Offers.All(o =>
                Matches(o, criteria) && eligibleProviderIds.Contains(o.Provider.Value)
            )
            || !result.PartialFailures.All(f =>
                f is not null
                && eligibleProviderIds.Contains(f.Provider)
                && !string.IsNullOrWhiteSpace(f.ErrorCode)
                && f.ElapsedMs >= 0
            )
        )
            return false;
        // Cold search reports an outage when every eligible provider fails; cached empty must agree.
        if (
            result.Offers.Count == 0
            && eligibleProviderIds.Count > 0
            && eligibleProviderIds.All(id => result.PartialFailures.Any(f => f.Provider == id))
        )
            return false;
        return OfferRanker.IsValid(result.Ranking, result.Offers);
    }

    // Deserialization bypasses creation factories. Rebuild for validation and compare, never rewrite.
    public static bool IsValidOffer(Offer offer)
    {
        if (
            offer is null
            || string.IsNullOrWhiteSpace(offer.Provider.Value)
            || offer.TotalAmount is null
            || offer.TotalAmount.Currency is null
            || Money.Create(offer.TotalAmount.Amount, offer.TotalAmount.Currency).IsError
            || CurrencyCode.Create(offer.TotalAmount.Currency.Value).IsError
            || offer.Itinerary is null
            || offer.Itinerary.TotalDuration is null
        )
            return false;
        var itinerary = Itinerary.Create(offer.Itinerary.Slices);
        if (
            itinerary.IsError
            || itinerary.Value.TotalDuration.Value != offer.Itinerary.TotalDuration.Value
        )
            return false;
        return offer switch
        {
            BookableOffer b => b.Party is { } party
                && !party.Validate().IsError
                && party.FirstDepartureLocalDate
                    == DateOnly.FromDateTime(itinerary.Value.Slices[0].DepartAt.DateTime)
                && !string.IsNullOrWhiteSpace(b.ProviderOfferRef)
                && b.FareConditions is not null,
            DeeplinkOffer d => d.DeeplinkUrl is { IsAbsoluteUri: true }
                && !string.IsNullOrWhiteSpace(d.PartnerName),
            _ => false,
        };
    }
}
