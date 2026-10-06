using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Endpoints;

internal static class CancellationEndpointResults
{
    internal static string? Fingerprint(HttpContext context) =>
        context.Items[CancellationRequestBodyGuard.FingerprintItem] as string;

    internal static IResult Reply(
        ErrorOr<CancellationStatusResult> result,
        HttpContext context,
        bool admitted
    )
    {
        context.Response.Headers.CacheControl = "no-store";
        if (result.IsError)
        {
            var first = result.FirstError;
            if (
                first.NumericType == 429
                && first.Metadata?.TryGetValue("retryAfter", out var retry) == true
            )
                context.Response.Headers.RetryAfter = Convert.ToString(
                    retry,
                    System.Globalization.CultureInfo.InvariantCulture
                );
            var problem = result.Errors.ToProblemDetails();
            if (
                first.NumericType == 429
                && first.Metadata?.TryGetValue("nextRefreshAt", out var next) == true
            )
                problem.Extensions["nextRefreshAt"] = next;
            return Results.Problem(problem);
        }
        var value = result.Value;
        var pending =
            admitted
            && (
                value.Operation?.ReadPending == true
                || value.Operation?.Phase
                    is "Preparing"
                        or "Accepted"
                        or "DispatchClaimed"
                        or "Unknown"
            );
        return Results.Json(
            CancellationStatusResponse.From(value),
            statusCode: pending ? 202 : 200
        );
    }
}
