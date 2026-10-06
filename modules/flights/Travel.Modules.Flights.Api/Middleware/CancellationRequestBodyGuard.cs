using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class CancellationRequestBodyGuard(RequestDelegate next)
{
    internal const string FingerprintItem = "Flights.Cancellation.RawFingerprint";

    internal static bool IsCancellationPath(PathString path) =>
        path.StartsWithSegments("/api/flights/cancellations", StringComparison.OrdinalIgnoreCase)
        || (
            path.StartsWithSegments("/api/flights/orders", StringComparison.OrdinalIgnoreCase)
            && (
                path.Value?.EndsWith("/cancellation", StringComparison.OrdinalIgnoreCase) == true
                || path.Value?.EndsWith("/cancellation-review", StringComparison.OrdinalIgnoreCase)
                    == true
            )
        );

    public async Task InvokeAsync(HttpContext context, IAuthorizationService authorization)
    {
        if (
            !HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Path.StartsWithSegments(
                "/api/flights/cancellations",
                out var remainder
            )
        )
        {
            await next(context);
            return;
        }
        var route = remainder.Value?.Trim('/').ToLowerInvariant() ?? "";
        if (
            route
            is not (
                "prepare"
                or "consent"
                or "abandon"
                or "refresh"
                or "review/refresh"
                or "review/resolve"
            )
        )
        {
            await next(context);
            return;
        }
        var policy = route.StartsWith("review/", StringComparison.Ordinal)
            ? "flights:cancellation-review"
            : "flights:book";
        if (!context.User.TryGetUserId(out var owner))
        {
            await context.WriteProblemDetailsAsync(IdentityProblemDetails.InvalidUserIdentity());
            return;
        }
        if (!(await authorization.AuthorizeAsync(context.User, policy)).Succeeded)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await BoundedRequestBody.ReadAsync(
            context,
            async bytes =>
            {
                if (!CancellationRequestValidation.Valid(bytes, route))
                {
                    await context.WriteProblemDetailsAsync(
                        new List<ErrorOr.Error>
                        {
                            CancellationRequestValidation.Invalid,
                        }.ToProblemDetails()
                    );
                    return;
                }
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(
                    Encoding.UTF8.GetBytes(
                        context.Request.Method
                            + "\n"
                            + context.Request.Path
                            + "\n"
                            + owner.ToString("N")
                            + "\n"
                    )
                );
                hash.AppendData(bytes.Span);
                context.Items[FingerprintItem] = Convert
                    .ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant();
                await next(context);
            },
            () =>
                context.WriteProblemDetailsAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = 413,
                        Type = "https://travel.local/errors/Flights.RequestTooLarge",
                        Title = "Request too large",
                        Detail = "Cancellation request exceeds the 16 KiB limit.",
                    }
                )
        );
    }
}
