using System.Diagnostics;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.NlSearch;

/// <summary>
/// Handles <see cref="NlSearchQuery"/> by forwarding to Travel.AI for parsing, then
/// invoking <see cref="SearchFlightsQuery"/> with the extracted structured criteria.
/// </summary>
public static class NlSearchHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<SearchResult>> Handle(
        NlSearchQuery q,
        IMessageBus bus,
        IFlightsMetrics metrics,
        ILogger<NlSearchQuery> log,
        CancellationToken ct
    )
    {
        // Derive the correlation id from the active OTel trace so that all log lines,
        // the AI cost-ledger row, and the search result are tied to the same trace.
        // Fall back to a fresh random GUID when no trace is active (e.g. in unit tests).
        var traceId = Activity.Current?.TraceId;
        var correlationId = traceId.HasValue
            ? Guid.ParseExact(traceId.Value.ToHexString(), "N")
            : Guid.NewGuid();

        var req = new NlSearchRequested(q.Query, correlationId, q.Locale);

        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["correlation_id"] = traceId?.ToString() ?? string.Empty,
            }
        );

        var nlSw = Stopwatch.StartNew();
        NlSearchParsed parsed;
        try
        {
            parsed = await bus.InvokeAsync<NlSearchParsed>(
                req,
                ct,
                timeout: TimeSpan.FromSeconds(6)
            );
        }
        catch (OperationCanceledException)
        {
            nlSw.Stop();
            throw;
        }
        catch (TimeoutException)
        {
            nlSw.Stop();
            metrics.RecordNlSearchDuration(nlSw.Elapsed.TotalMilliseconds);
            return FlightsErrors.NlSearchUnparseable;
        }
        catch (Exception ex)
        {
            nlSw.Stop();
            metrics.RecordNlSearchDuration(nlSw.Elapsed.TotalMilliseconds);
            log.LogWarning(
                ex,
                "NL-search AI call failed unexpectedly for correlation {CorrelationId}",
                req.CorrelationId
            );
            return FlightsErrors.NlSearchUnparseable;
        }

        nlSw.Stop();
        metrics.RecordNlSearchDuration(nlSw.Elapsed.TotalMilliseconds);

        // The model spent these tokens regardless of whether the parsed result is usable —
        // record usage before validation so the flights.nl_search.* metric stays accurate.
        metrics.RecordNlSearchUsage(parsed.InputTokens, parsed.OutputTokens, parsed.CostUsd);

        var origin = IataCode.Create(parsed.Origin);
        if (origin.IsError)
            return origin.FirstError;

        var dest = IataCode.Create(parsed.Destination);
        if (dest.IsError)
            return dest.FirstError;

        var cabin = CabinClass.Parse(parsed.CabinClass);
        if (cabin.IsError)
            return cabin.FirstError;

        var currency = CurrencyCode.Create(parsed.Currency);
        if (currency.IsError)
            return currency.FirstError;

        var sc = SearchCriteria.Create(
            origin.Value,
            dest.Value,
            parsed.DepartureDate,
            parsed.ReturnDate,
            parsed.PassengerCount,
            cabin.Value,
            currency.Value
        );
        if (sc.IsError)
            return sc.FirstError;

        return await bus.InvokeAsync<ErrorOr<SearchResult>>(new SearchFlightsQuery(sc.Value), ct);
    }
}
