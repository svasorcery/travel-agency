using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Api.Contracts;

namespace Travel.Modules.Trips.Api.Middleware;

internal static class TripOptionRoutes
{
    internal const string Preview = "/api/trips/option-preview";
    internal const string Availability = Preview + "/availability";
    private const int MaximumBytes = 4096;

    internal static bool Contains(PathString path) => path.StartsWithSegments(Preview);

    internal static async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
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
            || !string.Equals(path, Preview, StringComparison.OrdinalIgnoreCase)
        )
        {
            await TripsProblems.Problem("Trips.NotFound", 404).ExecuteAsync(context);
            return;
        }
        if (request.ContentLength > MaximumBytes)
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
        var bytes = new byte[MaximumBytes + 1];
        try
        {
            var length = await original.ReadAtLeastAsync(
                bytes,
                bytes.Length,
                throwOnEndOfStream: false,
                cancellationToken: context.RequestAborted
            );
            if (length > MaximumBytes)
            {
                await TooLarge(context);
                return;
            }
            if (!Valid(bytes.AsMemory(0, length)))
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

    private static bool Valid(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
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

    private static Task Invalid(HttpContext context) =>
        TripsProblems.Problem("Trips.InvalidInput", 400).ExecuteAsync(context);

    private static Task TooLarge(HttpContext context) =>
        TripsProblems.Problem("Trips.OptionRequestTooLarge", 413).ExecuteAsync(context);
}
