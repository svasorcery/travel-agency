using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application;

namespace Travel.Modules.Trips.Api.Middleware;

internal sealed record LinkPatch(long Revision, bool Add, Guid OrderId);

internal static class TripFlightLinkGuard
{
    internal static readonly object PatchKey = new();
    internal static readonly object OffsetKey = new();
    private const int MaxBody = 1024;

    internal static async Task RunAsync(HttpContext ctx, LinkRoute route, RequestDelegate next)
    {
        var request = ctx.Request;
        var patch = route == LinkRoute.Metadata && HttpMethods.IsPatch(request.Method);
        if (!patch && !HttpMethods.IsGet(request.Method))
        {
            await TripsProblems.Problem("Trips.MethodNotAllowed", 405).ExecuteAsync(ctx);
            return;
        }
        var original = ctx.RequestAborted;
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5),
            ctx.RequestServices.GetRequiredService<TimeProvider>()
        );
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(original, timeout.Token);
        ctx.RequestAborted = budget.Token;
        try
        {
            var invalid = TripsProblems.Problem("Trips.InvalidInput", 400);
            if (
                route != LinkRoute.Availability
                && (
                    !ctx.Request.Path.StartsWithSegments(
                        "/api/trips",
                        StringComparison.OrdinalIgnoreCase,
                        out var remaining
                    )
                    || remaining
                        .Value?.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()
                        is not { } trip
                    || !Guid.TryParseExact(trip, "D", out var tripId)
                    || tripId == Guid.Empty
                )
            )
            {
                await invalid.ExecuteAsync(ctx);
                return;
            }
            if (
                !TryOffset(request, route, out var offset)
                || (!patch && request.Headers.ContainsKey("If-Match"))
            )
            {
                await invalid.ExecuteAsync(ctx);
                return;
            }
            ctx.Items[OffsetKey] = offset;
            if (patch)
            {
                if (!request.Headers.TryGetValue("If-Match", out var values))
                {
                    await TripsProblems
                        .Problem("Trips.PreconditionRequired", 428)
                        .ExecuteAsync(ctx);
                    return;
                }
                if (values.Count != 1 || !ParseTag(values[0], out var revision))
                {
                    await invalid.ExecuteAsync(ctx);
                    return;
                }
                if (request.ContentLength > MaxBody)
                {
                    await TripsProblems.Problem("Trips.PatchTooLarge", 413).ExecuteAsync(ctx);
                    return;
                }
                if (
                    !MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
                    || !media.MediaType.Equals(
                        "application/json",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || (
                        media.Charset.HasValue
                        && !string.Equals(
                            media.Charset.Value?.Trim('"'),
                            "utf-8",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                {
                    await invalid.ExecuteAsync(ctx);
                    return;
                }
                var bytes = new byte[MaxBody + 1];
                try
                {
                    var length = await request.Body.ReadAtLeastAsync(
                        bytes.AsMemory(),
                        bytes.Length,
                        false,
                        budget.Token
                    );
                    if (length > MaxBody)
                    {
                        await TripsProblems.Problem("Trips.PatchTooLarge", 413).ExecuteAsync(ctx);
                        return;
                    }
                    if (ParsePatch(bytes.AsMemory(0, length), revision) is not { } operation)
                    {
                        await invalid.ExecuteAsync(ctx);
                        return;
                    }
                    ctx.Items[PatchKey] = operation;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }
            else if (
                request.ContentLength > 0
                || await request.Body.ReadAsync(new byte[1], budget.Token) != 0
            )
            {
                await invalid.ExecuteAsync(ctx);
                return;
            }
            if (
                route != LinkRoute.Availability
                && !await ctx
                    .RequestServices.GetRequiredService<ITripsReadiness>()
                    .IsReadyAsync(budget.Token)
            )
            {
                budget.Token.ThrowIfCancellationRequested();
                await TripsProblems.Problem("Trips.Unavailable", 503).ExecuteAsync(ctx);
                return;
            }
            await next(ctx);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested
                && !original.IsCancellationRequested
                && !ctx.Response.HasStarted
            )
        {
            ctx.RequestAborted = original;
            await TripsProblems.Problem("Trips.FlightLinksDeadline", 504).ExecuteAsync(ctx);
        }
        finally
        {
            ctx.RequestAborted = original;
            ctx.Items.Remove(PatchKey);
            ctx.Items.Remove(OffsetKey);
        }
    }

    private static bool TryOffset(HttpRequest request, LinkRoute route, out int offset)
    {
        offset = 0;
        if (request.Query.Count == 0)
            return true;
        return route == LinkRoute.Candidates
            && request.Query.Count == 1
            && request.Query.Keys.Single() == "offset"
            && request.Query["offset"] is { Count: 1 } values
            && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
            && offset is >= 0 and <= 10000
            && values[0] == offset.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ParseTag(string? tag, out long revision)
    {
        revision = 0;
        return tag is { Length: >= 6 and <= 24 }
            && tag.StartsWith("\"fl-", StringComparison.Ordinal)
            && tag[^1] == '"'
            && long.TryParse(
                tag.AsSpan(4, tag.Length - 5),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out revision
            )
            && tag == "\"fl-" + revision.ToString(CultureInfo.InvariantCulture) + "\"";
    }

    private static LinkPatch? ParsePatch(ReadOnlyMemory<byte> bytes, long revision)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
            var obj = doc.RootElement;
            if (
                obj.ValueKind != JsonValueKind.Object
                || obj.EnumerateObject().Count() != 2
                || !obj.TryGetProperty("op", out var op)
                || op.ValueKind != JsonValueKind.String
                || op.GetString() is not ("add" or "remove")
                || !obj.TryGetProperty("orderId", out var id)
                || id.ValueKind != JsonValueKind.String
                || !id.TryGetGuid(out var guid)
                || guid == Guid.Empty
            )
                return null;
            return new(revision, op.GetString() == "add", guid);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
