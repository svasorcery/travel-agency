using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Modules.Support.Api.Contracts;

public static class SupportProblemDetails
{
    public static ProblemDetails From(IReadOnlyList<Error> errors)
    {
        var error = errors[0];
        var status =
            error.Code == "Support.ClassificationBusy"
                ? 429
                : error.Type switch
                {
                    ErrorType.Validation => 400,
                    ErrorType.Unauthorized => 401,
                    ErrorType.Forbidden => 403,
                    ErrorType.NotFound => 404,
                    ErrorType.Conflict => 409,
                    _ => 503,
                };
        var problem = Fixed(status);
        if (error.Code is "Support.OrderLinkNotFound" or "Support.OrderLinkUnavailable")
        {
            problem.Type = "https://travel.local/errors/" + error.Code;
            problem.Title =
                error.Code == "Support.OrderLinkNotFound"
                    ? "Linked order not found"
                    : "Linked order unavailable";
        }
        return problem;
    }

    public static ProblemDetails Fixed(int status) =>
        new()
        {
            Status = status,
            Type =
                "https://travel.local/errors/Support."
                + (
                    status switch
                    {
                        400 => "InvalidInput",
                        401 => "Unauthenticated",
                        403 => "Forbidden",
                        404 => "NotFound",
                        409 => "Conflict",
                        413 => "RequestTooLarge",
                        429 => "ClassificationBusy",
                        _ => "Unavailable",
                    }
                ),
            Title = status switch
            {
                400 => "Invalid support request",
                401 => "Authentication required",
                403 => "Support access forbidden",
                404 => "Support resource not found",
                409 => "Support action conflict",
                413 => "Support request too large",
                429 => "Classification is busy",
                _ => "Support unavailable",
            },
        };
}
