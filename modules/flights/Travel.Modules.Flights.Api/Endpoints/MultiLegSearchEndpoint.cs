using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class MultiLegSearchEndpoint
{
    [WolverinePost("/api/flights/search/v2")]
    [AllowAnonymous]
    public static async Task<Results<Ok<SearchResponse>, ProblemHttpResult>> Post(
        SearchRequestV2 req,
        IMessageBus bus,
        HttpRequest httpRequest,
        [FromQuery] string? currency,
        CancellationToken ct
    )
    {
        var currencyCode = CurrencyCode.Create(currency ?? "RUB");
        if (currencyCode.IsError)
            return TypedResults.Problem(currencyCode.Errors.ToProblemDetails());
        if (req.Legs is null || req.Legs.Length is < 1 or > 4)
            return TypedResults.Problem(MultiLegSearchProblemDetails.Invalid());
        var cabin = CabinClass.Parse(req.CabinClass);
        if (cabin.IsError)
            return TypedResults.Problem(MultiLegSearchProblemDetails.Invalid());

        var legs = new RequestedFlightLeg[req.Legs.Length];
        for (var i = 0; i < legs.Length; i++)
        {
            var leg = req.Legs[i];
            if (leg is null)
                return TypedResults.Problem(MultiLegSearchProblemDetails.Invalid(i));
            var origin = IataCode.Create(leg.Origin);
            var destination = IataCode.Create(leg.Destination);
            if (origin.IsError || destination.IsError)
                return TypedResults.Problem(MultiLegSearchProblemDetails.Invalid(i));
            var requested = RequestedFlightLeg.Create(
                origin.Value,
                destination.Value,
                leg.DepartureDate
            );
            if (requested.IsError || (i > 0 && leg.DepartureDate < legs[i - 1].DepartureDate))
                return TypedResults.Problem(MultiLegSearchProblemDetails.Invalid(i));
            legs[i] = requested.Value;
        }
        var criteria = SearchCriteria.CreateMultiLeg(
            legs,
            req.PassengerCount,
            cabin.Value,
            currencyCode.Value,
            SearchEndpoint.ResolveLocale(httpRequest)
        );
        if (criteria.IsError)
            return TypedResults.Problem(MultiLegSearchProblemDetails.Invalid());
        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new SearchFlightsQuery(criteria.Value),
            ct
        );
        return result.IsError
            ? TypedResults.Problem(result.Errors.ToProblemDetails())
            : TypedResults.Ok(SearchResponse.From(result.Value));
    }
}

internal static class MultiLegSearchProblemDetails
{
    internal static ProblemDetails Invalid(int? legIndex = null)
    {
        var problem = new List<Error>
        {
            Error.Validation("Flights.MultiLegSearchInvalid", "Flight search request is invalid."),
        }.ToProblemDetails();
        if (legIndex is not null)
            problem.Extensions["legIndex"] = legIndex.Value;
        return problem;
    }
}
