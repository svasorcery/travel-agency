using ErrorOr;
using Travel.Modules.Flights.Application.Contracts;
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
        CancellationToken ct
    )
    {
        var req = new NlSearchRequested(q.Query, Guid.NewGuid(), q.Locale);

        NlSearchParsed parsed;
        try
        {
            parsed = await bus.InvokeAsync<NlSearchParsed>(
                req,
                ct,
                timeout: TimeSpan.FromSeconds(6)
            );
        }
        catch (TimeoutException)
        {
            return FlightsErrors.NlSearchUnparseable;
        }
        catch (Exception)
        {
            return FlightsErrors.NlSearchUnparseable;
        }

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
