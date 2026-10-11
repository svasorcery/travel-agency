using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public static class ResolveCustomerRefundReviewEndpoint
{
    [WolverinePost("/api/flights/orders/{aggregateId:guid}/customer-refund/review")]
    [Authorize("flights:customer-refund-review")]
    [ProducesResponseType(typeof(CustomerRefundStatusResult), StatusCodes.Status200OK)]
    public static async Task<IResult> Post(
        Guid aggregateId,
        CustomerRefundReviewRequest request,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var actor))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var parsed = request.ToInput();
        if (parsed.IsError)
            return Results.Problem(parsed.Errors.ToProblemDetails());
        var result = await bus.InvokeAsync<ErrorOr<CustomerRefundStatusResult>>(
            new ResolveCustomerRefundReviewCommand(aggregateId, actor, parsed.Value),
            ct
        );
        return CustomerRefundEndpointResults.Reply(result, context);
    }
}
