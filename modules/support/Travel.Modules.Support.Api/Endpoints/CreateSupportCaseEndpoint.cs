using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class CreateSupportCaseEndpoint
{
    [WolverinePut("/api/support/cases/{caseId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(SupportReceiptDto), 201)]
    [ProducesResponseType(typeof(SupportReceiptDto), 200)]
    public static async Task<IResult> Put(
        Guid caseId,
        CreateSupportCaseDto body,
        HttpContext context,
        [FromServices] ISupportCaseService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, false);
        return caller.IsError
            ? SupportEndpoint.Problem(caller.Errors)
            : SupportEndpoint.Mutation(
                await service.CreateAsync(
                    caller.Value,
                    new(caseId, body.MessageId, body.Title, body.Body, body.OrderId),
                    ct
                ),
                true
            );
    }
}
