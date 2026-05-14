using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Search;

public interface ISearchCache
{
    Task<IReadOnlyList<Offer>?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, IReadOnlyList<Offer> offers, TimeSpan ttl, CancellationToken ct);
}
