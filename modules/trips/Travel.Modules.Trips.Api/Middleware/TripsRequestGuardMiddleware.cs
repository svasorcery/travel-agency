using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Application;

namespace Travel.Modules.Trips.Api.Middleware;

public sealed class TripsRequestGuardMiddleware(RequestDelegate next, TripsFeature feature)
{
    public const int MaximumBytes = 128 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TripsResponsePolicyMiddleware.IsTrips(context.Request.Path))
        {
            await next(context);
            return;
        }
        if (!feature.Enabled)
        {
            await TripsProblems.Problem("Trips.NotFound", 404).ExecuteAsync(context);
            return;
        }
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
        {
            await TripsEndpoint.Failure(owner.FirstError).ExecuteAsync(context);
            return;
        }
        if (
            context.Request.Query.Any(item => item.Key != "cursor" || item.Value.Count != 1)
            || (
                context.Request.Query.Count != 0
                && (
                    !HttpMethods.IsGet(context.Request.Method)
                    || context.Request.Path.Value?.TrimEnd('/') != "/api/trips"
                )
            )
        )
        {
            await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);
            return;
        }
        if (
            HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method)
        )
        {
            var revision = TripsEndpoint.Revision(context);
            if (revision.IsError)
            {
                await TripsEndpoint.Failure(revision.FirstError).ExecuteAsync(context);
                return;
            }
        }
        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
        {
            if (context.Request.ContentLength > MaximumBytes)
            {
                await TripsProblems.Problem("Trips.PlanTooLarge", 400).ExecuteAsync(context);
                return;
            }
            if (
                !context.Request.HasJsonContentType()
                || !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media)
                || media.Charset.HasValue
                    && !string.Equals(
                        media.Charset.Value?.Trim('"'),
                        "utf-8",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            {
                await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);
                return;
            }
            var original = context.Request.Body;
            var bytes = new byte[MaximumBytes + 1];
            try
            {
                var length = 0;
                while (length < bytes.Length)
                {
                    var read = await original.ReadAsync(
                        bytes.AsMemory(length),
                        context.RequestAborted
                    );
                    if (read == 0)
                        break;
                    length += read;
                }
                if (length > MaximumBytes)
                {
                    await TripsProblems.Problem("Trips.PlanTooLarge", 400).ExecuteAsync(context);
                    return;
                }
                if (!Valid(bytes.AsMemory(0, length), HttpMethods.IsPut(context.Request.Method)))
                {
                    await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);
                    return;
                }
                using var body = new MemoryStream(bytes, 0, length, false);
                context.Request.Body = body;
                await ReadyAndNext(context);
            }
            finally
            {
                context.Request.Body = original;
                CryptographicOperations.ZeroMemory(bytes);
            }
            return;
        }
        await ReadyAndNext(context);
    }

    private async Task ReadyAndNext(HttpContext context)
    {
        var readiness = context.RequestServices.GetRequiredService<ITripsReadiness>();
        if (!await readiness.IsReadyAsync(context.RequestAborted))
        {
            await TripsProblems.Problem("Trips.Unavailable", 503).ExecuteAsync(context);
            return;
        }
        await next(context);
    }

    public static bool Valid(ReadOnlyMemory<byte> bytes, bool replacement)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes.Span);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions { MaxDepth = 12 }
            );
            var root = document.RootElement;
            if (
                !Fields(
                    root,
                    replacement ? ["title", "brief", "days"] : ["title", "brief"],
                    replacement ? ["title", "brief", "days"] : ["title", "brief"]
                ) || !Text(root.GetProperty("title"))
            )
                return false;
            var brief = root.GetProperty("brief");
            if (
                !Fields(
                    brief,
                    [
                        "destinationId",
                        "originId",
                        "startDate",
                        "endDate",
                        "adults",
                        "currency",
                        "totalBudget",
                        "interests",
                        "pace",
                    ],
                    [
                        "destinationId",
                        "startDate",
                        "endDate",
                        "adults",
                        "currency",
                        "interests",
                        "pace",
                    ]
                )
            )
                return false;
            foreach (
                var key in new[] { "destinationId", "startDate", "endDate", "currency", "pace" }
            )
                if (!Text(brief.GetProperty(key)))
                    return false;
            if (!Date(brief.GetProperty("startDate")) || !Date(brief.GetProperty("endDate")))
                return false;
            if (
                !brief.GetProperty("adults").TryGetInt32(out _)
                || brief.GetProperty("interests").ValueKind != JsonValueKind.Array
                || brief.GetProperty("interests").GetArrayLength() > 5
                || brief.GetProperty("interests").EnumerateArray().Any(item => !Text(item))
                || !OptionalText(brief, "originId")
                || !OptionalText(brief, "totalBudget")
            )
                return false;
            if (!replacement)
                return true;
            var days = root.GetProperty("days");
            if (days.ValueKind != JsonValueKind.Array || days.GetArrayLength() > 14)
                return false;
            foreach (var day in days.EnumerateArray())
            {
                if (
                    !Fields(day, ["date", "blocks"], ["date", "blocks"])
                    || !Date(day.GetProperty("date"))
                )
                    return false;
                var blocks = day.GetProperty("blocks");
                if (blocks.ValueKind != JsonValueKind.Array || blocks.GetArrayLength() > 8)
                    return false;
                foreach (var block in blocks.EnumerateArray())
                {
                    if (
                        !Fields(
                            block,
                            ["blockId", "title", "note", "startTime", "endTime", "estimate"],
                            ["blockId", "title"]
                        )
                        || !Text(block.GetProperty("blockId"))
                        || !block.GetProperty("blockId").TryGetGuid(out var id)
                        || id == Guid.Empty
                        || !Text(block.GetProperty("title"))
                        || !OptionalText(block, "note")
                        || !OptionalText(block, "startTime")
                        || !OptionalText(block, "endTime")
                    )
                        return false;
                    if (
                        block.TryGetProperty("estimate", out var estimate)
                        && estimate.ValueKind != JsonValueKind.Null
                        && (
                            !Fields(estimate, ["amount", "currency"], ["amount", "currency"])
                            || !Text(estimate.GetProperty("amount"))
                            || !Text(estimate.GetProperty("currency"))
                        )
                    )
                        return false;
                }
            }
            return true;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or ArgumentException
                        or InvalidOperationException
                        or DecoderFallbackException
            )
        {
            return false;
        }
    }

    private static bool Date(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && DateOnly.TryParseExact(
            value.GetString(),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _
        );

    private static bool Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && ValidDecodedText(value.GetString());

    private static bool ValidDecodedText(string? value)
    {
        if (value is null)
            return false;
        try
        {
            StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool OptionalText(JsonElement value, string key) =>
        !value.TryGetProperty(key, out var item)
        || item.ValueKind == JsonValueKind.Null
        || Text(item);

    private static bool Fields(JsonElement value, string[] allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal) || !seen.Add(field.Name))
                return false;
        return required.All(seen.Contains);
    }
}
