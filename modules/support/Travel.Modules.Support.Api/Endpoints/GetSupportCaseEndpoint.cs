using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class GetSupportCaseEndpoint
{
    [WolverineGet("/api/support/cases/{caseId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(SupportCaseDto), 200)]
    public static async Task<IResult> Get(
        Guid caseId,
        HttpContext context,
        [FromServices] ISupportCaseService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, false);
        if (caller.IsError)
            return SupportEndpoint.Problem(caller.Errors);
        var result = await service.GetCaseAsync(caller.Value, caseId, ct);
        return result.IsError
            ? SupportEndpoint.Problem(result.Errors)
            : Results.Ok(SupportCaseDto.From(result.Value));
    }
}
