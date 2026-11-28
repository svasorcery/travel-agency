using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Api.Contracts;

namespace Travel.Modules.Trips.Api.Middleware;

internal static class TripOptionRoutes
{
    internal const string Preview = "/api/trips/option-preview";
    internal const string Availability = Preview + "/availability";
    internal const string Save = "/api/trips/saved-options";
    internal const string FormatHeader = "X-Trips-Option-Save";

    internal static bool IsSave(PathString path) => path.StartsWithSegments(Save);

    internal static bool Contains(PathString path) => path.StartsWithSegments(Preview);

    internal static async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        var save = IsSave(request.Path);
        var maximumBytes = save ? 128 * 1024 : 4096;
        if (
            request.Headers.TryGetValue(FormatHeader, out var format)
            && (
                save || !HttpMethods.IsPost(request.Method) || format.Count != 1 || format[0] != "1"
            )
        )
        {
            await Invalid(context);
            return;
        }
        var path = request.Path.Value?.TrimEnd('/');
        if (request.QueryString.HasValue)
        {
            await Invalid(context);
            return;
        }
        if (
            HttpMethods.IsGet(request.Method)
            && string.Equals(path, Availability, StringComparison.OrdinalIgnoreCase)
        )
        {
            if (await TripsRequestGuardMiddleware.HasBody(context))
                await Invalid(context);
            else
                await next(context);
            return;
        }
        if (
            !HttpMethods.IsPost(request.Method)
            || !string.Equals(path, save ? Save : Preview, StringComparison.OrdinalIgnoreCase)
        )
        {
            await TripsProblems.Problem("Trips.NotFound", 404).ExecuteAsync(context);
            return;
        }
        if (request.ContentLength > maximumBytes)
        {
            await TooLarge(context);
            return;
        }
        if (!TripsRequestGuardMiddleware.JsonContent(request))
        {
            await Invalid(context);
            return;
        }
        var original = request.Body;
        var bytes = new byte[maximumBytes + 1];
        try
        {
            var length = await original.ReadAtLeastAsync(
                bytes,
                bytes.Length,
                throwOnEndOfStream: false,
                cancellationToken: context.RequestAborted
            );
            if (length > maximumBytes)
            {
                await TooLarge(context);
                return;
            }
            if (!Valid(bytes.AsMemory(0, length), save))
            {
                await Invalid(context);
                return;
            }
            using var body = new MemoryStream(bytes, 0, length, writable: false);
            request.Body = body;
            await next(context);
        }
        finally
        {
            request.Body = original;
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool Valid(ReadOnlyMemory<byte> body, bool save)
    {
        try
        {
            using var document = JsonDocument.Parse(
                body,
                new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false }
            );
            var root = document.RootElement;
            if (save)
                return ValidSelection(root);
            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().Count() == 1
                && root.TryGetProperty("brief", out var brief)
                && TripsRequestGuardMiddleware.ValidBrief(brief);
        }
        catch (Exception e)
            when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidSelection(JsonElement root)
    {
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("selectionToken", out var token)
            || token.ValueKind != JsonValueKind.String
            || token.GetString() is not { Length: > 0 and <= 65536 }
        )
            return false;
        return root.EnumerateObject()
            .All(p =>
                p.NameEquals("selectionToken")
                || (
                    p.NameEquals("title")
                    && (
                        p.Value.ValueKind == JsonValueKind.Null
                        || p.Value.ValueKind == JsonValueKind.String
                    )
                )
            );
    }

    private static Task Invalid(HttpContext context) =>
        TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);

    private static Task TooLarge(HttpContext context) =>
        TripsProblems.Problem("Trips.OptionRequestTooLarge", 413).ExecuteAsync(context);
}
