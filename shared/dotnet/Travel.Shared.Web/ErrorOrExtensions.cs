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
            Type = $"https://travel.local/errors/{first.Code}",
            Title = first.Type.ToString(),
            Status = MapStatus(first),
            Detail = first.Description,
            Extensions =
            {
                ["errors"] = errors.Select(e => new
                {
                    e.Code,
                    e.Description,
                    Type = e.Type.ToString(),
                }),
            },
        };
    }

    /// <summary>
    /// Maps an <see cref="Error"/> to an HTTP status code.
    /// Standard <see cref="ErrorType"/> values use the canonical mapping. For
    /// <see cref="Error.Custom"/> errors the numeric type IS the intended HTTP status code
    /// (e.g. <c>Error.Custom(503, …)</c> → 503 Service Unavailable). Any other custom
    /// value falls back to 500.
    /// </summary>
    private static int MapStatus(Error error) =>
        error.Type switch
        {
            ErrorType.Validation => 400,
            ErrorType.NotFound => 404,
            ErrorType.Conflict => 409,
            ErrorType.Unauthorized => 401,
            ErrorType.Forbidden => 403,
            ErrorType.Failure => 500,
            ErrorType.Unexpected => 500,
            // Error.Custom(type: N, …): treat N as the HTTP status when it is a valid
            // 4xx/5xx code; otherwise fall back to 500.
            _ when error.NumericType is >= 400 and <= 599 => error.NumericType,
            _ => 500,
        };
}
