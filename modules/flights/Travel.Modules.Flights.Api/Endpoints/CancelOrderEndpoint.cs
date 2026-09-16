using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class CancelOrderEndpoint
{
    [WolverinePost("/api/flights/orders/{aggregateId:guid}/cancel")]
    [Authorize("flights:book")]
    public static async Task<IResult> Post(
        Guid aggregateId,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!httpContext.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());

        var cancelResult = await bus.InvokeAsync<ErrorOr<CancelledOrderResult>>(
            new CancelOrderCommand(aggregateId, userId),
            ct
        );
        if (cancelResult.IsError)
            return Results.Problem(cancelResult.Errors.ToProblemDetails());

        var snapshot = cancelResult.Value.Snapshot;
        return Results.Ok(
            new OrderResponse(
                cancelResult.Value.AggregateId,
                cancelResult.Value.Status,
                snapshot.TotalAmount.Amount,
                snapshot.TotalAmount.Currency.Value,
                ItineraryDto.From(snapshot.Itinerary),
                snapshot.TicketNumbers.ToArray(),
                snapshot.BookedAt,
                snapshot.TicketedAt,
                snapshot.CancelledAt,
                snapshot.RefundedAt
            )
        );
    }
}
