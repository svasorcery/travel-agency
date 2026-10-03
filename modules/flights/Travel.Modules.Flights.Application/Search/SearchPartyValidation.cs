using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public static class SearchPartyValidation
{
    public static bool IsValid(SearchResult result, SearchCriteria criteria) =>
        result.SkippedProviders is not null
        && result.SkippedProviders.All(p =>
            p is not null
            && !string.IsNullOrWhiteSpace(p.Provider)
            && p.ReasonCode == "passenger-count-unsupported"
            && criteria.PassengerCount > 1
        )
        && result.Offers is not null
        && result.Offers.All(o => Matches(o, criteria));

    public static bool Matches(Offer o, SearchCriteria criteria) =>
        o switch
        {
            BookableOffer b => b.Party is { } party
                && !party.Validate().IsError
                && party.PassengerCount == criteria.PassengerCount,
            DeeplinkOffer => criteria.PassengerCount == 1,
            _ => false,
        };
}
