using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class ConsentCancellationEndpoint
{
    [WolverinePost("/api/flights/cancellations/consent")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(CancellationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CancellationStatusResponse), StatusCodes.Status202Accepted)]
    public static async Task<IResult> Post(
        ConsentCancellationRequest req,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var fingerprint = CancellationEndpointResults.Fingerprint(context);
        if (fingerprint is null)
            return Results.Problem(
                new List<Error> { CancellationRequestValidation.Invalid }.ToProblemDetails()
            );
        var result = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
            new ConsentCancellationCommand(
                req.AggregateId,
                userId,
                req.OperationId,
                req.ExpectedOperationRevision,
                req.TermsRevision,
                req.TermsHash,
                req.NoticeVersion,
                req.Accepted,
                fingerprint
            ),
            ct
        );
        return CancellationEndpointResults.Reply(result, context, true);
    }
}
