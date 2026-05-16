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
        IFxRates fxRates,
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

        // Normalise all offers to the requested currency before dedup/rank
        var normalised = await NormalizeOffersAsync(
            allOffers,
            query.Criteria.Currency,
            fxRates,
            log,
            ct
        );

        var deduped = OfferDeduplicator.Dedup(normalised);
        var ranked = OfferRanker.Rank(deduped);
        await cache.SetAsync(key, ranked, TimeSpan.FromMinutes(5), ct);

        // Feed the rolling-window gauge: partial fill when at least one provider had a failure.
        metrics.RecordSearchPartialFill(failures.Count > 0);
        // Each offer returned to the caller counts toward the offer-to-book conversion gauge.
        foreach (var _ in ranked)
            metrics.RecordOfferShown();

        return new SearchResult(ranked, failures);
    }

    private static async Task<IReadOnlyList<Offer>> NormalizeOffersAsync(
        IReadOnlyList<Offer> offers,
        CurrencyCode targetCurrency,
        IFxRates fxRates,
        ILogger log,
        CancellationToken ct
    )
    {
        var normalised = new List<Offer>(offers.Count);
        foreach (var offer in offers)
        {
            if (offer.TotalAmount.Currency == targetCurrency)
            {
                normalised.Add(offer);
                continue;
            }

            var convertResult = await fxRates.ConvertAsync(offer.TotalAmount, targetCurrency, ct);
            if (convertResult.IsError)
            {
                log.LogWarning(
                    "FX conversion failed for offer {OfferId} ({From}->{To}): {Error}; keeping original amount",
                    offer.Id.Value,
                    offer.TotalAmount.Currency.Value,
                    targetCurrency.Value,
                    convertResult.FirstError.Description
                );
                normalised.Add(offer);
                continue;
            }

            normalised.Add(offer.WithAmount(convertResult.Value));
        }
        return normalised;
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
