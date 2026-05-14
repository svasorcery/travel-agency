using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class IdempotencyKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IIdempotencyStore store)
    {
        if (!IsTargetedRoute(ctx.Request))
        {
            await next(ctx);
            return;
        }

        if (
            !ctx.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader)
            || !Guid.TryParse(keyHeader.ToString(), out var keyGuid)
        )
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { code = "Flights.IdempotencyKey.Missing" });
            return;
        }

        var userId = ctx.User.GetUserId();
        var key = new IdempotencyKey(keyGuid.ToString("N"));
        var route = ctx.Request.Path.ToString();

        ctx.Request.EnableBuffering();
        var bodyHash = await HashBodyAsync(ctx.Request);

        var cached = await store.TryGetAsync(key, userId, route, ctx.RequestAborted);
        if (cached is not null)
        {
            ctx.Response.StatusCode = cached.ResponseStatus;
            ctx.Response.Headers["Idempotency-Replay"] = "true";
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(cached.ResponseBody, ctx.RequestAborted);
            return;
        }

        var check = await store.CheckOrConflictAsync(
            key,
            userId,
            route,
            bodyHash,
            ctx.RequestAborted
        );
        if (check.IsError)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(check.FirstError);
            return;
        }

        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await next(ctx);
            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer).ReadToEndAsync(ctx.RequestAborted);
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, ctx.RequestAborted);

            var responseHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(responseBody))
            );
            await store.SaveAsync(
                key,
                userId,
                route,
                bodyHash,
                responseHash,
                ctx.Response.StatusCode,
                responseBody,
                ctx.RequestAborted
            );
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }
    }

    private static bool IsTargetedRoute(HttpRequest r)
    {
        if (r.Method != HttpMethods.Post)
            return false;
        var p = r.Path.Value ?? string.Empty;
        return p.StartsWith("/api/flights/orders", StringComparison.OrdinalIgnoreCase)
            && (
                p.EndsWith("/hold", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/confirm", StringComparison.OrdinalIgnoreCase)
                || p.Contains("/cancel", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static async Task<string> HashBodyAsync(HttpRequest r)
    {
        r.Body.Position = 0;
        using var ms = new MemoryStream();
        await r.Body.CopyToAsync(ms);
        r.Body.Position = 0;
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }
}
