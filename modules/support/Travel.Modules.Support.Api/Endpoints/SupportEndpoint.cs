using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Travel.Shared.Web;

namespace Travel.Modules.Support.Api.Endpoints;

internal static class SupportEndpoint
{
    internal static ErrorOr<SupportCaller> Caller(HttpContext context, bool isOperator)
    {
        if (
            !context.User.TryGetUserId(out var subject)
            || context.User.Identity?.IsAuthenticated != true
        )
            return SupportErrors.Unauthenticated;
        var hasPermission = context
            .User.FindAll("scope")
            .Any(x =>
                x.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("support:operator", StringComparer.Ordinal)
            );
        var party = context.User.FindFirst("azp")?.Value;
        if (isOperator && (!hasPermission || party != "travel-support-operator"))
            return SupportErrors.Forbidden;
        return new SupportCaller(
            subject,
            isOperator ? SupportActorKind.Operator : SupportActorKind.Customer,
            true,
            hasPermission,
            party
        );
    }

    internal static IResult Problem(IReadOnlyList<Error> errors) =>
        Results.Problem(SupportProblemDetails.From(errors));

    internal static IResult Mutation(
        ErrorOr<SupportMutationResult> result,
        bool creation = false
    ) =>
        result.IsError ? Problem(result.Errors)
        : creation && !result.Value.IsReplay
            ? Results.Json(SupportReceiptDto.From(result.Value.Receipt), statusCode: 201)
        : Results.Ok(SupportReceiptDto.From(result.Value.Receipt));

    internal static ErrorOr<SupportCaseFilters> Filters(HttpContext context)
    {
        foreach (var item in context.Request.Query)
            if (
                item.Key is not ("state" or "category" or "priority" or "cursor")
                || item.Value.Count != 1
            )
                return SupportErrors.InvalidInput;
        if (
            !TryEnum<CaseState>(context.Request.Query["state"].FirstOrDefault(), out var state)
            || !TryEnum<ManualCategory>(
                context.Request.Query["category"].FirstOrDefault(),
                out var category
            )
            || !TryEnum<SupportPriority>(
                context.Request.Query["priority"].FirstOrDefault(),
                out var priority
            )
        )
            return SupportErrors.InvalidInput;
        return new SupportCaseFilters(state, category, priority);
    }

    internal static bool TryEnum<T>(string? text, out T? value)
        where T : struct, Enum
    {
        value = null;
        if (text is null)
            return true;
        if (
            !Enum.TryParse<T>(text, false, out var parsed)
            || !Enum.IsDefined(parsed)
            || parsed.ToString() != text
        )
            return false;
        value = parsed;
        return true;
    }

    internal static ErrorOr<string?> Cursor(HttpContext context, bool list)
    {
        if (!list && context.Request.Query.Any(x => x.Key != "cursor" || x.Value.Count != 1))
            return SupportErrors.InvalidInput;
        var cursor = context.Request.Query["cursor"].FirstOrDefault();
        return cursor is { Length: > 2048 }
            ? SupportErrors.InvalidInput
            : ErrorOrFactory.From<string?>(cursor);
    }
}
