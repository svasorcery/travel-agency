using ErrorOr;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Core.Providers;

public interface IFlightAncillaryProvider
{
    Task<ErrorOr<AncillaryCatalogFacts>> ReadCatalogAsync(
        string providerOfferRef,
        bool includeSeats,
        CancellationToken ct
    );
}
