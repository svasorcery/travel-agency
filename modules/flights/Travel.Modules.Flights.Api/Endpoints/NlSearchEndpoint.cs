using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Handlers.NlSearch;
using Travel.Modules.Flights.Application.Queries;
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
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new NlSearchQuery(req.Query, req.Locale),
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
