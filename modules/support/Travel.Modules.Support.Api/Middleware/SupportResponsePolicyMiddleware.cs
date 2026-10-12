using Microsoft.AspNetCore.Http;
using Travel.Modules.Support.Api.Contracts;

namespace Travel.Modules.Support.Api.Middleware;

public sealed class SupportResponsePolicyMiddleware(RequestDelegate next)
{
    public static bool IsSupportPath(PathString path) =>
        path.StartsWithSegments("/api/support", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsSupportPath(context.Request.Path))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }
        await next(context);
        if (
            IsSupportPath(context.Request.Path)
            && !context.Response.HasStarted
            && context.Response.StatusCode is 400 or 401 or 403 or 404 or 409 or 413 or 503
        )
            await context.Response.WriteAsJsonAsync(
                SupportProblemDetails.Fixed(context.Response.StatusCode),
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted
            );
    }
}
