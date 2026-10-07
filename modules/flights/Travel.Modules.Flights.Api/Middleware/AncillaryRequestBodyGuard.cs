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
            || path is not ("/api/flights/orders/quote" or "/api/flights/orders/ancillaries")
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
                        Detail = "Booking request exceeds the 16 KiB limit.",
                        Type = "https://travel.local/errors/Flights.RequestTooLarge",
                    }
                )
        );
    }
}
