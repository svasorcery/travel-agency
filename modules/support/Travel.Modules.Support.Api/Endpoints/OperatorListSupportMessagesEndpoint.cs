using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class OperatorListSupportMessagesEndpoint
{
    [WolverineGet("/api/support/operator/cases/{caseId:guid}/messages")]
    [Authorize(SupportModule.OperatorPolicy)]
    [ProducesResponseType(typeof(SupportMessagePageDto), 200)]
    public static async Task<IResult> Get(
        Guid caseId,
        HttpContext context,
        [FromServices] ISupportCaseService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, true);
        if (caller.IsError)
            return SupportEndpoint.Problem(caller.Errors);
        var cursor = SupportEndpoint.Cursor(context, false);
        if (cursor.IsError)
            return SupportEndpoint.Problem(cursor.Errors);
        var result = await service.ListMessagesAsync(caller.Value, caseId, cursor.Value, ct);
        return result.IsError
            ? SupportEndpoint.Problem(result.Errors)
            : Results.Ok(
                new SupportMessagePageDto(
                    result.Value.CaseId,
                    result.Value.Items.Select(SupportMessageDto.From).ToArray(),
                    result.Value.NextCursor
                )
            );
    }
}
