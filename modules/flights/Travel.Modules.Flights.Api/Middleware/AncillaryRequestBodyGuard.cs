using Microsoft.AspNetCore.Http;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class AncillaryRequestBodyGuard(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/');
        if (
            !HttpMethods.IsPost(context.Request.Method)
            || !(
                string.Equals(path, "/api/flights/orders/quote", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    path,
                    "/api/flights/orders/ancillaries",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(path, "/api/flights/search/nl", StringComparison.OrdinalIgnoreCase)
            )
        )
            return next(context);
        return BoundedRequestBody.ReadAsync(
            context,
            _ => next(context),
            () =>
                context.WriteProblemDetailsAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = 413,
                        Title = "Request too large",
                        Detail = "Request exceeds the 16 KiB limit.",
                        Type = "https://travel.local/errors/Flights.RequestTooLarge",
                    }
                )
        );
    }
}
