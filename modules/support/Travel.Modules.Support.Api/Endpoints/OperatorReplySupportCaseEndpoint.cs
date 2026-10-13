using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class OperatorReplySupportCaseEndpoint
{
    [WolverinePut("/api/support/operator/cases/{caseId:guid}/messages/{messageId:guid}")]
    [Authorize(SupportModule.OperatorPolicy)]
    [ProducesResponseType(typeof(SupportReceiptDto), 200)]
    public static async Task<IResult> Put(
        Guid caseId,
        Guid messageId,
        ReplySupportCaseDto body,
        HttpContext context,
        [FromServices] ISupportCaseService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, true);
        if (caller.IsError)
            return SupportEndpoint.Problem(caller.Errors);
        if (body.Kind != "Reply")
            return SupportEndpoint.Problem([SupportErrors.InvalidInput]);
        var kind = SupportActionKind.OperatorReply;
        return SupportEndpoint.Mutation(
            await service.ReplyAsync(
                caller.Value,
                new(caseId, messageId, body.Body, body.ExpectedRevision, kind),
                ct
            )
        );
    }
}
