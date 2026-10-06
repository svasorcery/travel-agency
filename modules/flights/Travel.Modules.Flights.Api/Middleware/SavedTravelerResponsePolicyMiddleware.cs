using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class SavedTravelerResponsePolicyMiddleware(RequestDelegate next)
{
    internal static bool IsProfilePath(PathString path) =>
        path.StartsWithSegments("/api/flights/travelers", StringComparison.OrdinalIgnoreCase);

    public Task InvokeAsync(HttpContext context)
    {
        if (
            IsProfilePath(context.Request.Path)
            || CancellationRequestBodyGuard.IsCancellationPath(context.Request.Path)
        )
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }
        return next(context);
    }
}
