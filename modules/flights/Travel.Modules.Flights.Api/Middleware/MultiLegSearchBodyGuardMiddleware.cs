using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class MultiLegSearchBodyGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> options
    )
    {
        if (
            !HttpMethods.IsPost(context.Request.Method)
            || !string.Equals(
                context.Request.Path.Value?.TrimEnd('/'),
                "/api/flights/search/v2",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            await next(context);
            return;
        }
        await BoundedRequestBody.ReadAsync(
            context,
            async bytes =>
            {
                var (valid, index) = IsSafeJson(bytes, options.Value.SerializerOptions);
                if (!valid)
                {
                    await context.WriteProblemDetailsAsync(
                        MultiLegSearchProblemDetails.Invalid(index)
                    );
                    return;
                }
                await next(context);
            },
            () =>
                context.WriteProblemDetailsAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = 413,
                        Type = "https://travel.local/errors/Flights.RequestTooLarge",
                        Title = "Request too large",
                        Detail = "Flight search request body exceeds the 16 KiB limit.",
                    }
                )
        );
    }

    private static (bool Valid, int? Index) IsSafeJson(
        ReadOnlyMemory<byte> bytes,
        JsonSerializerOptions options
    )
    {
        try
        {
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    MaxDepth = options.MaxDepth,
                    AllowTrailingCommas = options.AllowTrailingCommas,
                    CommentHandling = options.ReadCommentHandling,
                }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (false, null);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            JsonElement legs = default;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    return (false, null);
                switch (property.Name.ToLowerInvariant())
                {
                    case "legs":
                        legs = property.Value;
                        if (
                            legs.ValueKind != JsonValueKind.Array
                            || legs.GetArrayLength() is < 1 or > 4
                        )
                            return (false, null);
                        break;
                    case "passengercount":
                        if (
                            property.Value.ValueKind != JsonValueKind.Number
                            || !property.Value.TryGetInt32(out _)
                        )
                            return (false, null);
                        break;
                    case "cabinclass":
                        if (property.Value.ValueKind != JsonValueKind.String)
                            return (false, null);
                        break;
                    default:
                        return (false, null);
                }
            }
            if (legs.ValueKind != JsonValueKind.Array)
                return (false, null);
            var index = 0;
            foreach (var leg in legs.EnumerateArray())
            {
                if (leg.ValueKind != JsonValueKind.Object)
                    return (false, index);
                var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in leg.EnumerateObject())
                {
                    if (
                        !fields.Add(property.Name)
                        || property.Value.ValueKind != JsonValueKind.String
                    )
                        return (false, index);
                    switch (property.Name.ToLowerInvariant())
                    {
                        case "origin":
                        case "destination":
                            break;
                        case "departuredate":
                            if (
                                !DateOnly.TryParseExact(
                                    property.Value.GetString(),
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out _
                                )
                            )
                                return (false, index);
                            break;
                        default:
                            return (false, index);
                    }
                }
                if (fields.Count != 3)
                    return (false, index);
                index++;
            }
            return (true, null);
        }
        catch (JsonException)
        {
            // Serializer exceptions may contain submitted names or values. Do not log them.
            return (false, null);
        }
    }
}
