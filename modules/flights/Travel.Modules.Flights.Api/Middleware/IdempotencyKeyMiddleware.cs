using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class IdempotencyKeyMiddleware(RequestDelegate next)
{
    private const int MaximumBodyBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext ctx, IIdempotencyStore store)
    {
        if (!IsTargetedRoute(ctx.Request))
        {
            await next(ctx);
            return;
        }

        if (!ctx.User.TryGetUserId(out var userId))
        {
            await ctx.WriteProblemDetailsAsync(IdentityProblemDetails.InvalidUserIdentity());
            return;
        }

        if (
            !ctx.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader)
            || !Guid.TryParse(keyHeader.ToString(), out var keyGuid)
        )
        {
            await ctx.WriteProblemDetailsAsync(
                new List<Error>
                {
                    Error.Validation(
                        "Flights.IdempotencyKey.Missing",
                        "A valid Idempotency-Key header is required."
                    ),
                }.ToProblemDetails()
            );
            return;
        }
        var key = new IdempotencyKey(keyGuid.ToString("N"));
        var route = ctx.Request.Path.ToString();
        if (route.TrimEnd('/').EndsWith("/cancel", StringComparison.OrdinalIgnoreCase))
            ctx.Response.Headers.CacheControl = "no-store";

        if (ctx.Request.ContentLength is > MaximumBodyBytes)
        {
            await WriteBodyTooLargeAsync(ctx);
            return;
        }

        var originalRequestBody = ctx.Request.Body;
        var bytes = new byte[MaximumBodyBytes + 1];
        try
        {
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await originalRequestBody.ReadAsync(
                    bytes.AsMemory(length),
                    ctx.RequestAborted
                );
                if (read == 0)
                    break;
                length += read;
            }
            if (length > MaximumBodyBytes)
            {
                await WriteBodyTooLargeAsync(ctx);
                return;
            }

            // Bound before hashing/model binding. Never use spill-to-disk buffering for passenger PII.
            using var body = new MemoryStream(bytes, 0, length, writable: false);
            ctx.Request.Body = body;
            var bodyHash = HashRequest(ctx.Request, bytes.AsSpan(0, length));
            await InvokeBufferedAsync(ctx, store, userId, key, route, bodyHash);
        }
        finally
        {
            ctx.Request.Body = originalRequestBody;
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static Task WriteBodyTooLargeAsync(HttpContext ctx) =>
        ctx.WriteProblemDetailsAsync(
            new List<Error>
            {
                Error.Custom(
                    StatusCodes.Status413PayloadTooLarge,
                    "Flights.RequestTooLarge",
                    "Booking request body exceeds the 16 KiB limit."
                ),
            }.ToProblemDetails()
        );

    private async Task InvokeBufferedAsync(
        HttpContext ctx,
        IIdempotencyStore store,
        Guid userId,
        IdempotencyKey key,
        string route,
        string bodyHash
    )
    {
        // The hash mixes HTTP method + route + body so that the same idempotency
        // key on a different method or route cannot accidentally collide. Route
        // is already part of the store's lookup, but mixing it into the hash too
        // closes the window where two distinct routes share the same body bytes.

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
                await ctx.WriteProblemDetailsAsync(
                    new List<Error>
                    {
                        Error.Conflict(
                            "Flights.IdempotencyInFlight",
                            "Request with the same Idempotency-Key is already in progress."
                        ),
                    }.ToProblemDetails()
                );
                return;

            case BeginOutcome.BodyConflict:
                await ctx.WriteProblemDetailsAsync(
                    new List<Error>
                    {
                        Error.Conflict(
                            "Flights.IdempotencyConflict",
                            "Idempotency key reused with a different payload."
                        ),
                    }.ToProblemDetails()
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
        // Route matching accepts trailing slashes. Only recognition is normalized;
        // the original method/path/body remain part of the existing fingerprint.
        var p = (r.Path.Value ?? string.Empty).TrimEnd('/');
        // Use EndsWith / segment-aware matches so that crafted paths like
        // "/orders/cancel/foo" cannot match "/cancel".
        return p.StartsWith("/api/flights/orders", StringComparison.OrdinalIgnoreCase)
            && (
                p.EndsWith("/hold", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/confirm", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("/cancel", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string HashRequest(HttpRequest request, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path.Value}\n");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix);
        hash.AppendData(body);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
