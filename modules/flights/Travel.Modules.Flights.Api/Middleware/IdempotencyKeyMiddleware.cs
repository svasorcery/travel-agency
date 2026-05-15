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
        // The hash mixes HTTP method + route + body so that the same idempotency
        // key on a different method or route cannot accidentally collide. Route
        // is already part of the store's lookup, but mixing it into the hash too
        // closes the window where two distinct routes share the same body bytes.
        var bodyHash = await HashRequestAsync(ctx.Request);

        // Atomically reserve the row (or detect a winner). Started => proceed,
        // Replay => write the cached response, InFlight => 409, BodyConflict => 409.
        var begin = await store.TryBeginAsync(key, userId, route, bodyHash, ctx.RequestAborted);

        switch (begin.Outcome)
        {
            case BeginOutcome.Replay:
                var replay = begin.Replay!;
                ctx.Response.StatusCode = replay.ResponseStatus;
                ctx.Response.Headers["Idempotency-Replay"] = "true";
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(replay.ResponseBody, ctx.RequestAborted);
                return;

            case BeginOutcome.InFlight:
                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Flights.IdempotencyInFlight",
                        description = "Request with the same Idempotency-Key is already in progress.",
                    },
                    ctx.RequestAborted
                );
                return;

            case BeginOutcome.BodyConflict:
                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(
                    new
                    {
                        code = "Flights.IdempotencyConflict",
                        description = "Idempotency key reused with a different payload.",
                    },
                    ctx.RequestAborted
                );
                return;

            case BeginOutcome.Started:
                break;
        }

        // Started: invoke the handler. Capture the response so we can decide whether
        // to cache (2xx only) or abandon (everything else, so a retry can re-execute).
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

            var status = ctx.Response.StatusCode;
            if (status is >= 200 and < 300)
            {
                var responseHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(responseBody))
                );
                await store.CompleteAsync(
                    key,
                    userId,
                    route,
                    responseHash,
                    status,
                    responseBody,
                    ctx.RequestAborted
                );
            }
            else
            {
                // Non-2xx: drop the in-flight row so a retry with the same key
                // re-executes the handler. The error response is still sent to
                // the client — only the cache row is discarded.
                await store.AbandonAsync(key, userId, route, ctx.RequestAborted);
            }
        }
        catch
        {
            // Exception bypassed normal response — abandon the in-flight row.
            try
            {
                await store.AbandonAsync(key, userId, route, CancellationToken.None);
            }
            catch
            {
                // Swallow secondary failure: the primary exception is more useful.
            }
            throw;
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
        // Use EndsWith / segment-aware matches so that crafted paths like
        // "/orders/cancel/foo" cannot match "/cancel".
        return p.StartsWith("/api/flights/orders", StringComparison.OrdinalIgnoreCase)
            && (
                p.EndsWith("/hold", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/confirm", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/cancel", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static async Task<string> HashRequestAsync(HttpRequest r)
    {
        r.Body.Position = 0;
        using var ms = new MemoryStream();
        await r.Body.CopyToAsync(ms);
        r.Body.Position = 0;

        // Mix the HTTP method into the hash. The route is already part of the
        // store's composite lookup, but folding it in here is defence-in-depth.
        var prefix = Encoding.UTF8.GetBytes($"{r.Method}\n{r.Path.Value}\n");
        var bodyBytes = ms.ToArray();
        var combined = new byte[prefix.Length + bodyBytes.Length];
        Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
        Buffer.BlockCopy(bodyBytes, 0, combined, prefix.Length, bodyBytes.Length);
        return Convert.ToHexString(SHA256.HashData(combined));
    }
}
