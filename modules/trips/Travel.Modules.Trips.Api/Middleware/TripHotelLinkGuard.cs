using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application;

namespace Travel.Modules.Trips.Api.Middleware;

internal enum HotelLinkRoute
{
    None,
    Availability,
    Metadata,
    Status,
}

internal sealed record HotelLinkPatch(long Revision, Guid? ReservationId);

internal static class TripHotelLinkGuard
{
    private const int MaximumPatchBytes = 1024;
    internal static readonly object PatchKey = new();

    internal static HotelLinkRoute Classify(PathString path)
    {
        if (
            !path.StartsWithSegments("/api/trips", StringComparison.OrdinalIgnoreCase, out var rest)
        )
            return HotelLinkRoute.None;
        var value = rest.Value?.TrimEnd('/') ?? "";
        if (value.Equals("/hotel-link/availability", StringComparison.OrdinalIgnoreCase))
            return HotelLinkRoute.Availability;
        var end = value.Length > 1 ? value.IndexOf('/', 1) : -1;
        if (end <= 1)
            return HotelLinkRoute.None;
        var suffix = value.AsSpan(end);
        if (suffix.Equals("/hotel-link", StringComparison.OrdinalIgnoreCase))
            return HotelLinkRoute.Metadata;
        return suffix.Equals("/hotel-link/status", StringComparison.OrdinalIgnoreCase)
            ? HotelLinkRoute.Status
            : HotelLinkRoute.None;
    }

    internal static async Task RunAsync(HttpContext ctx, HotelLinkRoute route, RequestDelegate next)
    {
        var request = ctx.Request;
        var isPatch = route == HotelLinkRoute.Metadata && HttpMethods.IsPatch(request.Method);
        if (!isPatch && !HttpMethods.IsGet(request.Method))
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
            if (request.Query.Count != 0 || (!isPatch && request.Headers.ContainsKey("If-Match")))
            {
                await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(ctx);
                return;
            }
            if (isPatch)
            {
                if (!request.Headers.TryGetValue("If-Match", out var tags))
                {
                    await TripsProblems
                        .Problem("Trips.PreconditionRequired", 428)
                        .ExecuteAsync(ctx);
                    return;
                }
                if (tags.Count != 1 || !TryParseTag(tags[0], out var revision) || !IsJson(request))
                {
                    await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(ctx);
                    return;
                }
                if (await ReadPatchAsync(ctx, revision, budget.Token) is not { } operation)
                    return;
                ctx.Items[PatchKey] = operation;
            }
            else if (
                request.ContentLength > 0
                || await request.Body.ReadAsync(new byte[1], budget.Token) != 0
            )
            {
                await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(ctx);
                return;
            }
            if (
                route != HotelLinkRoute.Availability
                && !await ctx
                    .RequestServices.GetRequiredService<ITripsReadiness>()
                    .IsReadyAsync(budget.Token)
            )
            {
                await TripsProblems.Problem("Trips.Unavailable", 503).ExecuteAsync(ctx);
                return;
            }
            await next(ctx);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested && !original.IsCancellationRequested)
        {
            if (ctx.Response.HasStarted)
                ctx.Abort();
            else
            {
                ctx.RequestAborted = original;
                await TripsProblems.Problem("Trips.Unavailable", 503).ExecuteAsync(ctx);
            }
        }
        finally
        {
            ctx.RequestAborted = original;
        }
    }

    internal static string Tag(long revision) =>
        $"\"hl-{revision.ToString(CultureInfo.InvariantCulture)}\"";

    private static bool TryParseTag(string? value, out long revision)
    {
        revision = -1;
        return value is { Length: >= 6 }
            && value.StartsWith("\"hl-", StringComparison.Ordinal)
            && value[^1] == '"'
            && long.TryParse(
                value.AsSpan(4, value.Length - 5),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out revision
            )
            && revision >= 0
            && value == Tag(revision);
    }

    private static bool IsJson(HttpRequest request) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
        && media.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        && (
            !media.Charset.HasValue
            || string.Equals(
                media.Charset.Value?.Trim('"'),
                "utf-8",
                StringComparison.OrdinalIgnoreCase
            )
        );

    private static async Task<HotelLinkPatch?> ReadPatchAsync(
        HttpContext ctx,
        long revision,
        CancellationToken ct
    )
    {
        var request = ctx.Request;
        if (request.ContentLength > MaximumPatchBytes)
        {
            await TripsProblems.Problem("Trips.PatchTooLarge", 413).ExecuteAsync(ctx);
            return null;
        }
        var bytes = new byte[MaximumPatchBytes + 1];
        try
        {
            var length = await request.Body.ReadAtLeastAsync(
                bytes.AsMemory(),
                bytes.Length,
                false,
                ct
            );
            if (length > MaximumPatchBytes)
            {
                await TripsProblems.Problem("Trips.PatchTooLarge", 413).ExecuteAsync(ctx);
                return null;
            }
            if (ParsePatch(bytes.AsMemory(0, length), revision) is { } operation)
                return operation;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
        await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(ctx);
        return null;
    }

    private static HotelLinkPatch? ParsePatch(ReadOnlyMemory<byte> bytes, long revision)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("op", out var op)
                || op.ValueKind != JsonValueKind.String
            )
                return null;
            var count = root.EnumerateObject().Count();
            if (op.ValueEquals("remove") && count == 1)
                return new(revision, null);
            if (
                op.ValueEquals("add")
                && count == 2
                && root.TryGetProperty("reservationId", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.TryGetGuid(out var guid)
                && guid != Guid.Empty
            )
                return new(revision, guid);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
