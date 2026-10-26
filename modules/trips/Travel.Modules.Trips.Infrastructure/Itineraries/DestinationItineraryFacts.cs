using ErrorOr;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Trips.Infrastructure.Itineraries;

// Two explicit consumers of the same reader, not a runtime provider registry.
[TestOnly]
public sealed class DestinationItineraryFacts(
    CuratedItineraryFacts moscow,
    CuratedItineraryFacts saintPetersburg
) : IItineraryFactSource
{
    public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
        DestinationSelection destination,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return destination?.DestinationId switch
        {
            "moscow" => moscow.ReadAsync(destination, cancellationToken),
            "saint-petersburg" => saintPetersburg.ReadAsync(destination, cancellationToken),
            _ => Task.FromResult<ErrorOr<ValidatedItineraryFacts>>(
                Error.Failure("Trips.ItineraryUnavailable", "Itinerary facts are unavailable.")
            ),
        };
    }
}
