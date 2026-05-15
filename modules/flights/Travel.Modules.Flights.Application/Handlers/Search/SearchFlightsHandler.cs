using ErrorOr;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Search;

public static class SearchFlightsHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<SearchResult>> Handle(
        SearchFlightsQuery query,
        IEnumerable<IFlightSearchProvider> providers,
        ISearchCache cache,
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<SearchFlightsQuery> log,
        CancellationToken ct
    )
    {
        var key = SearchCacheKey.Build(query.Criteria);
        var cached = await cache.TryGetAsync(key, ct);
        if (cached is not null)
            return new SearchResult(cached, Array.Empty<ProviderFailure>());

        var providerList = providers.ToList();
        var tasks = providerList
            .Select(p => RunWithTimeout(p, query.Criteria, time, metrics, log, ct))
            .ToList();
        var results = await Task.WhenAll(tasks);

        var allOffers = results
            .Where(r => r.Offers is not null)
            .SelectMany(r => r.Offers!)
            .ToList();
        var failures = results.Where(r => r.Failure is not null).Select(r => r.Failure!).ToList();

        if (allOffers.Count == 0)
            return failures.Count == providerList.Count
                ? FlightsErrors.ProviderUnavailable("all")
                : (ErrorOr<SearchResult>)new SearchResult(Array.Empty<Offer>(), failures);

        var deduped = OfferDeduplicator.Dedup(allOffers);
        var ranked = OfferRanker.Rank(deduped);
        await cache.SetAsync(key, ranked, TimeSpan.FromMinutes(5), ct);
        return new SearchResult(ranked, failures);
    }

    private static async Task<(
        IReadOnlyList<Offer>? Offers,
        ProviderFailure? Failure
    )> RunWithTimeout(
        IFlightSearchProvider provider,
        SearchCriteria c,
        TimeProvider time,
        IFlightsMetrics metrics,
        ILogger log,
        CancellationToken ct
    )
    {
        var started = time.GetTimestamp();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var result = await provider.SearchAsync(c, cts.Token);
            var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
            metrics.RecordSearchLatency(
                elapsedMs,
                provider.Id.Value,
                result.IsError ? "error" : "ok"
            );
            if (result.IsError)
            {
                metrics.RecordSearchError(provider.Id.Value);
                return (
                    null,
                    new ProviderFailure(provider.Id.Value, result.FirstError.Code, (long)elapsedMs)
                );
            }
            return (result.Value, null);
        }
        catch (OperationCanceledException)
        {
            var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
            metrics.RecordSearchLatency(elapsedMs, provider.Id.Value, "timeout");
            metrics.RecordSearchError(provider.Id.Value);
            return (null, new ProviderFailure(provider.Id.Value, "Timeout", (long)elapsedMs));
        }
        catch (Exception ex)
        {
            var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
            log.LogWarning(
                ex,
                "Provider {Provider} threw an unexpected exception during search fan-out",
                provider.Id.Value
            );
            metrics.RecordSearchLatency(elapsedMs, provider.Id.Value, "error");
            metrics.RecordSearchError(provider.Id.Value);
            return (
                null,
                new ProviderFailure(provider.Id.Value, "ProviderFailure", (long)elapsedMs)
            );
        }
    }
}
