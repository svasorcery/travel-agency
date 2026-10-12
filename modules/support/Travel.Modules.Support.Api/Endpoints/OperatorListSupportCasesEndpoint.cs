using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Endpoints;

public sealed class OperatorListSupportCasesEndpoint
{
    [WolverineGet("/api/support/operator/cases")]
    [Authorize(SupportModule.OperatorPolicy)]
    [ProducesResponseType(typeof(SupportCasePageDto), 200)]
    public static async Task<IResult> Get(
        HttpContext context,
        [FromServices] ISupportCaseService service,
        CancellationToken ct
    )
    {
        var caller = SupportEndpoint.Caller(context, true);
        if (caller.IsError)
            return SupportEndpoint.Problem(caller.Errors);
        var filters = SupportEndpoint.Filters(context);
        if (filters.IsError)
            return SupportEndpoint.Problem(filters.Errors);
        var cursor = SupportEndpoint.Cursor(context, true);
        if (cursor.IsError)
            return SupportEndpoint.Problem(cursor.Errors);
        var result = await service.ListCasesAsync(caller.Value, filters.Value, cursor.Value, ct);
        return result.IsError
            ? SupportEndpoint.Problem(result.Errors)
            : Results.Ok(
                new SupportCasePageDto(
                    result.Value.Items.Select(SupportCaseDto.From).ToArray(),
                    result.Value.NextCursor
                )
            );
    }
}
