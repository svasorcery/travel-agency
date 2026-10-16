using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Errors;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class NlSearchEndpoint
{
    // Must match SearchEndpoint.SupportedLocales — locale normalisation is endpoint-layer-only.
    private static readonly string[] SupportedLocales = ["ru", "en"];

    [WolverinePost("/api/flights/search/nl")]
    [AllowAnonymous]
    [EnableRateLimiting("flights-nl-search")]
    public static async Task<IResult> Post(
        NlSearchRequest req,
        IMessageBus bus,
        IOptionsMonitor<FlightsFeatureFlags> flags,
        HttpRequest httpRequest,
        CancellationToken ct
    )
    {
        if (!flags.CurrentValue.NlSearch.Enabled)
            return Results.Problem(
                new List<Error> { FlightsErrors.NlSearchDisabled }.ToProblemDetails()
            );

        if (string.IsNullOrWhiteSpace(req.Query) || req.Query.Length > 1000)
            return Results.Problem(
                statusCode: 400,
                title: "Invalid search query",
                detail: "Provide between 1 and 1000 characters."
            );

        var locale = httpRequest.ResolveLocale(SupportedLocales, defaultLocale: "ru");

        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new NlSearchQuery(req.Query, locale),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(SearchResponse.From(result.Value));
    }
}
