using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class ActSupportCaseEndpoint
{
    [WolverinePut("/api/support/operator/cases/{caseId:guid}/actions/{actionId:guid}")]
    [Authorize(SupportModule.OperatorPolicy)]
    [ProducesResponseType(typeof(SupportReceiptDto), 200)]
    public static async Task<IResult> Put(
        Guid caseId,
        Guid actionId,
        ActSupportCaseDto body,
        HttpContext context,
        [FromServices] ISupportCaseService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, true);
        if (caller.IsError)
            return SupportEndpoint.Problem(caller.Errors);
        if (
            body.Kind is not ("Resolve" or "SetTriage")
            || !SupportEndpoint.TryEnum<ManualCategory>(body.Category, out var category)
            || !SupportEndpoint.TryEnum<SupportPriority>(body.Priority, out var priority)
        )
            return SupportEndpoint.Problem([SupportErrors.InvalidInput]);
        var kind = body.Kind == "Resolve" ? SupportActionKind.Resolve : SupportActionKind.SetTriage;
        return SupportEndpoint.Mutation(
            await service.ActAsync(
                caller.Value,
                new(caseId, actionId, body.ExpectedRevision, kind, category, priority),
                ct
            )
        );
    }
}
