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
using Travel.Modules.Trips.Core;

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
        var hotelRoute = TripHotelLinkGuard.Classify(context.Request.Path);
        if (
            hotelRoute != HotelLinkRoute.None
            && context.RequestServices.GetService<TripHotelLinksFeature>()?.Enabled != true
        )
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
        if (TripOptionRoutes.Contains(context.Request.Path))
        {
            await TripOptionRoutes.GuardAsync(context, next);
            return;
        }
        if (hotelRoute != HotelLinkRoute.None)
        {
            await TripHotelLinkGuard.RunAsync(context, hotelRoute, next);
            return;
        }
        var briefRoute = TripsBriefExtractionRoutes.Classify(context.Request);
        var linkRoute = TripFlightLinkRoutes.Classify(context.Request.Path);
        if (linkRoute != LinkRoute.None)
        {
            await TripFlightLinkGuard.RunAsync(context, linkRoute, next);
            return;
        }
        if (briefRoute != TripsBriefExtractionRoute.None)
        {
            await TripsBriefExtractionGuard.HandleAsync(context, briefRoute, owner.Value, next);
            return;
        }
        var itineraryRoute = TripsItineraryRoutes.Classify(context.Request);
        var itineraryVersion = 1;
        if (itineraryRoute != TripsItineraryRoute.None)
        {
            var protocol = TripsItineraryProtocol.Version(context.Request);
            if (protocol.IsError)
            {
                await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);
                return;
            }
            itineraryVersion = protocol.Value;
        }
        if (itineraryRoute == TripsItineraryRoute.Availability)
        {
            if (
                TripsItineraryProtocol
                    .AvailabilityDestination(context.Request, itineraryVersion)
                    .IsError || await HasBody(context)
            )
                await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);
            else
                await next(context);
            return;
        }
        if (itineraryRoute == TripsItineraryRoute.Candidate)
        {
            var revision = TripsEndpoint.Revision(context);
            if (revision.IsError)
            {
                await TripsEndpoint.Failure(revision.FirstError).ExecuteAsync(context);
                return;
            }
            if (
                context.Request.Query.Count != 0
                || !JsonContent(context.Request)
                || !await EmptyObject(context)
            )
            {
                await TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);
                return;
            }
            await ReadyAndNext(context);
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
                var replacement = HttpMethods.IsPut(context.Request.Method);
                var workspaceV2 =
                    replacement
                    && context
                        .Request.Path.Value?.TrimEnd('/')
                        .EndsWith("/workspace-v2", StringComparison.OrdinalIgnoreCase) == true;
                if (!Valid(bytes.AsMemory(0, length), replacement, workspaceV2))
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

    internal static bool JsonContent(HttpRequest request) =>
        request.HasJsonContentType()
        && MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
        && (
            !media.Charset.HasValue
            || string.Equals(
                media.Charset.Value?.Trim('"'),
                "utf-8",
                StringComparison.OrdinalIgnoreCase
            )
        );

    internal static async Task<bool> HasBody(HttpContext context)
    {
        if (context.Request.ContentLength > 0)
            return true;
        return await context.Request.Body.ReadAsync(new byte[1], context.RequestAborted) != 0;
    }

    private static async Task<bool> EmptyObject(HttpContext context)
    {
        if (context.Request.ContentLength > 1024)
            return false;
        var bytes = new byte[1025];
        try
        {
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(
                    bytes.AsMemory(length),
                    context.RequestAborted
                );
                if (read == 0)
                    break;
                length += read;
            }
            if (length > 1024)
                return false;
            StrictUtf8.GetCharCount(bytes, 0, length);
            using var document = JsonDocument.Parse(
                bytes.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 16 }
            );
            return document.RootElement.ValueKind == JsonValueKind.Object
                && !document.RootElement.EnumerateObject().Any();
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
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

    private static bool Valid(ReadOnlyMemory<byte> bytes, bool replacement, bool workspaceV2)
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
                    workspaceV2 ? ["title", "brief", "days", "manualStays"]
                        : replacement ? ["title", "brief", "days", "manualStay"]
                        : ["title", "brief"],
                    workspaceV2 ? ["title", "brief", "days", "manualStays"]
                        : replacement ? ["title", "brief", "days"]
                        : ["title", "brief"]
                ) || !Text(root.GetProperty("title"))
            )
                return false;
            if (!ValidBrief(root.GetProperty("brief")))
                return false;
            if (!replacement)
                return true;
            if (workspaceV2)
            {
                var stays = root.GetProperty("manualStays");
                if (
                    stays.ValueKind != JsonValueKind.Array
                    || stays.GetArrayLength() > TripPlan.MaximumManualStays
                    || stays.EnumerateArray().Any(item => !ValidStay(item))
                )
                    return false;
            }
            if (
                root.TryGetProperty("manualStay", out var stay)
                && stay.ValueKind != JsonValueKind.Null
                && !ValidStay(stay)
            )
                return false;
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

    internal static bool ValidBrief(JsonElement brief)
    {
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
                ["destinationId", "startDate", "endDate", "adults", "currency", "interests", "pace"]
            )
        )
            return false;
        foreach (var key in new[] { "destinationId", "startDate", "endDate", "currency", "pace" })
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
        return true;
    }

    private static bool ValidStay(JsonElement stay) =>
        Fields(
            stay,
            ["placeName", "checkInDate", "checkOutDate"],
            ["placeName", "checkInDate", "checkOutDate"]
        )
        && Text(stay.GetProperty("placeName"))
        && Date(stay.GetProperty("checkInDate"))
        && Date(stay.GetProperty("checkOutDate"));

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
