using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Shared.Web;

public static class ErrorOrExtensions
{
    public static ProblemDetails ToProblemDetails(this List<Error> errors)
    {
        var first = errors[0];
        return new ProblemDetails
        {
            Type   = $"https://travel.local/errors/{first.Code}",
            Title  = first.Type.ToString(),
            Status = first.Type switch
            {
                ErrorType.Validation   => 400,
                ErrorType.NotFound     => 404,
                ErrorType.Conflict     => 409,
                ErrorType.Unauthorized => 401,
                ErrorType.Forbidden    => 403,
                _                      => 500,
            },
            Detail     = first.Description,
            Extensions = { ["errors"] = errors.Select(e => new { e.Code, e.Description, Type = e.Type.ToString() }) },
        };
    }
}
