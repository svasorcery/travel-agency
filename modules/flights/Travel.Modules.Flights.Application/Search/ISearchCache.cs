using Travel.Modules.Flights.Application.Queries;

namespace Travel.Modules.Flights.Application.Search;

public interface ISearchCache
{
    Task<SearchResult?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, SearchResult result, TimeSpan ttl, CancellationToken ct);
}
