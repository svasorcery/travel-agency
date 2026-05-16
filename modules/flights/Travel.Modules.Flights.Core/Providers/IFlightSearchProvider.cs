using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Core.Providers;

public interface IFlightSearchProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(SearchCriteria criteria, CancellationToken ct);
}
