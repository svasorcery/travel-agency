using ErrorOr;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Application.Itineraries;

public interface IItineraryFactSource
{
    Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
        DestinationSelection destination,
        CancellationToken cancellationToken = default
    );
}
