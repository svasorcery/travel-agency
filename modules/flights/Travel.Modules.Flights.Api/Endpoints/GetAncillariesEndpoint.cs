using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class GetAncillariesEndpoint
{
    [WolverinePost("/api/flights/orders/ancillaries")]
    [Authorize("flights:book")]
    public static async Task<IResult> Post(
        AncillaryCatalogRequest request,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.User.TryGetUserId(out var owner))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var result = await bus.InvokeAsync<ErrorOr<AncillaryCatalogResult>>(
            new GetAncillariesQuery(
                request.AggregateId,
                owner,
                request.QuoteRevision,
                request.IncludeSeats
            ),
            ct
        );
        return result.IsError
            ? Results.Problem(result.Errors.ToProblemDetails())
            : Results.Ok(AncillaryCatalogResponse.From(result.Value));
    }
}
