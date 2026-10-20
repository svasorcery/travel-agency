using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Rail.Api.Middleware;

public sealed class RailResponsePolicy(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (
            context.Request.Path.StartsWithSegments("/api/rail", StringComparison.OrdinalIgnoreCase)
        )
            context.Response.Headers.CacheControl = "no-store";
        await next(context);
    }
}
