using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    [WolverinePost("/api/flights/search/nl")]
    [AllowAnonymous]
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

        var locale = SearchEndpoint.ResolveLocale(httpRequest);

        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new NlSearchQuery(req.Query, locale),
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
