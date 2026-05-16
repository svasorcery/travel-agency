using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public interface IDeeplinkOfferCache
{
    Task<IReadOnlyList<DeeplinkOffer>?> TryGetAsync(string criteriaHash, CancellationToken ct);

    Task SetAsync(string criteriaHash, IReadOnlyList<DeeplinkOffer> offers, CancellationToken ct);

    Task PurgeExpiredAsync(CancellationToken ct);
}
