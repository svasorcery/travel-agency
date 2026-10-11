using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class CustomerRefundRequestBodyGuard(RequestDelegate next)
{
    internal static bool IsPath(PathString path) =>
        path.StartsWithSegments("/api/flights/orders", StringComparison.OrdinalIgnoreCase)
        && (
            path.Value?.EndsWith("/customer-refund", StringComparison.OrdinalIgnoreCase) == true
            || path.Value?.EndsWith("/customer-refund/review", StringComparison.OrdinalIgnoreCase)
                == true
        );

    public async Task InvokeAsync(HttpContext context, IAuthorizationService authorization)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !IsPath(context.Request.Path))
        {
            await next(context);
            return;
        }
        if (!context.User.TryGetUserId(out _))
        {
            await context.WriteProblemDetailsAsync(IdentityProblemDetails.InvalidUserIdentity());
            return;
        }
        if (
            !(
                await authorization.AuthorizeAsync(context.User, "flights:customer-refund-review")
            ).Succeeded
        )
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await BoundedRequestBody.ReadAsync(
            context,
            async bytes =>
            {
                if (!CustomerRefundRequestValidation.Valid(bytes))
                {
                    await context.WriteProblemDetailsAsync(
                        new List<ErrorOr.Error>
                        {
                            CustomerRefundRequestValidation.Invalid,
                        }.ToProblemDetails()
                    );
                    return;
                }
                await next(context);
            },
            () =>
                context.WriteProblemDetailsAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = 413,
                        Type = "https://travel.local/errors/Flights.RequestTooLarge",
                        Title = "Request too large",
                        Detail = "Customer refund review exceeds the 16 KiB limit.",
                    }
                )
        );
    }
}
