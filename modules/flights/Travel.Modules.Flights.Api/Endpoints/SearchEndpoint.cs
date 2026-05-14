using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class SearchEndpoint
{
    [WolverinePost("/api/flights/search")]
    [AllowAnonymous]
    public static async Task<IResult> Post(SearchRequest req, IMessageBus bus, CancellationToken ct)
    {
        var origin = IataCode.Create(req.Origin);
        if (origin.IsError)
            return Results.Problem(origin.Errors.ToProblemDetails());

        var dest = IataCode.Create(req.Destination);
        if (dest.IsError)
            return Results.Problem(dest.Errors.ToProblemDetails());

        var cabin = CabinClass.Parse(req.CabinClass);
        if (cabin.IsError)
            return Results.Problem(cabin.Errors.ToProblemDetails());

        var currency = CurrencyCode.Create(req.Currency);
        if (currency.IsError)
            return Results.Problem(currency.Errors.ToProblemDetails());

        var sc = SearchCriteria.Create(
            origin.Value,
            dest.Value,
            req.DepartureDate,
            req.ReturnDate,
            req.PassengerCount,
            cabin.Value,
            currency.Value
        );
        if (sc.IsError)
            return Results.Problem(sc.Errors.ToProblemDetails());

        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new SearchFlightsQuery(sc.Value),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(
            new SearchResponse(
                result.Value.Offers.Select(OfferDto.From).ToArray(),
                result
                    .Value.PartialFailures.Select(f => new PartialFailureDto(
                        f.Provider,
                        f.ErrorCode,
                        f.ElapsedMs
                    ))
                    .ToArray()
            )
        );
    }
}
