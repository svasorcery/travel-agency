using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public static class GetCustomerRefundStatusEndpoint
{
    [WolverineGet("/api/flights/orders/{aggregateId:guid}/customer-refund")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(CustomerRefundStatusResult), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid aggregateId,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var result = await bus.InvokeAsync<ErrorOr<CustomerRefundStatusResult>>(
            new GetCustomerRefundStatusQuery(aggregateId, userId),
            ct
        );
        return CustomerRefundEndpointResults.Reply(result, context);
    }
}
