using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class QuoteOfferEndpoint
{
    [WolverinePost("/api/flights/orders/quote")]
    [AllowAnonymous]
    public static async Task<IResult> Post(
        QuoteOfferRequest req,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var providerId = new ProviderId(req.Provider);
        var result = await bus.InvokeAsync<ErrorOr<QuotedOfferResult>>(
            new QuoteOfferCommand(req.ProviderOfferRef, providerId, req.AggregateId),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(
            new QuotedOfferResponse(result.Value.AggregateId, OfferDto.From(result.Value.Offer))
        );
    }
}
