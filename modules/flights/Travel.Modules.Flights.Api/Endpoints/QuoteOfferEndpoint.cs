using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        CancellationToken ct,
        HttpContext httpContext,
        [FromServices] IAuthorizationService? authorization = null
    )
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        Guid? owner = httpContext.User.TryGetUserId(out var userId) ? userId : null;
        var canBook =
            authorization is not null
            && (await authorization.AuthorizeAsync(httpContext.User, "flights:book")).Succeeded;
        if (req.Selections is { Length: > 0 } && !canBook)
            return owner is null ? Results.Unauthorized() : Results.Forbid();
        var providerId = new ProviderId(req.Provider);
        var result = await bus.InvokeAsync<ErrorOr<QuotedOfferResult>>(
            new QuoteOfferCommand(
                req.ProviderOfferRef,
                providerId,
                req.AggregateId,
                req.PassengerCount,
                owner,
                req.Selections,
                canBook
            ),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        var v = result.Value;
        return Results.Ok(
            new QuotedOfferResponse(
                AggregateId: v.AggregateId,
                Offer: OfferDto.From(
                    v.Purchase is { } purchase
                        ? v.Offer with
                        {
                            TotalAmount = purchase.Total,
                        }
                        : v.Offer
                ),
                FareConditions: FareConditionsDto.From(v.Offer.FareConditions),
                Binding: QuoteBindingDto.From(v.Binding),
                PriceChanged: v.PriceChanged,
                OldAmount: v.OldAmount?.Amount,
                OldCurrency: v.OldAmount?.Currency.Value,
                NewAmount: v.NewAmount?.Amount,
                NewCurrency: v.NewAmount?.Currency.Value,
                Purchase: v.Purchase is { } selected ? BookingPurchaseDto.From(selected) : null
            )
        );
    }
}
