using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Queries;

public sealed record SearchFlightsQuery(SearchCriteria Criteria);

public sealed record SearchResult(
    IReadOnlyList<Offer> Offers,
    IReadOnlyList<ProviderFailure> PartialFailures
);

public sealed record ProviderFailure(string Provider, string ErrorCode, long ElapsedMs);
