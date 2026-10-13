using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class RequestSupportClassificationEndpoint
{
    [WolverinePost("/api/support/operator/cases/{caseId:guid}/classification-suggestion")]
    [Authorize(SupportModule.OperatorPolicy)]
    [ProducesResponseType(typeof(SupportClassificationResultDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 429)]
    public static async Task<IResult> Post(
        Guid caseId,
        RequestSupportClassificationDto body,
        HttpContext context,
        [FromServices] ISupportClassificationService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, true);
        if (caller.IsError)
            return SupportEndpoint.Problem(caller.Errors);
        var result = await service.SuggestAsync(
            caller.Value,
            new(caseId, body.RequestId, body.ExpectedRevision),
            ct
        );
        if (result.IsError)
        {
            if (result.FirstError.Code == "Support.ClassificationBusy")
                context.Response.Headers.RetryAfter = "60";
            return SupportEndpoint.Problem(result.Errors);
        }
        return Results.Ok(SupportClassificationResultDto.From(result.Value));
    }
}
