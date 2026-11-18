using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Serialization;

public sealed class TripFlightLinkCodec : ITripFlightLinkCodec
{
    public ErrorOr<string> Serialize(TripFlightLinks collection)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("links");
            foreach (var link in collection.Links)
            {
                writer.WriteStartObject();
                writer.WriteString("orderId", link.OrderId);
                writer.WriteString(
                    "linkedAt",
                    link.LinkedAt.ToString("O", CultureInfo.InvariantCulture)
                );
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public ErrorOr<TripFlightLinks> Deserialize(string plaintext, TripFlightLinkBinding binding)
    {
        if (
            string.IsNullOrEmpty(plaintext)
            || plaintext.Length > ITripFlightLinkProtector.MaximumBytes
            || Encoding.UTF8.GetByteCount(plaintext) > ITripFlightLinkProtector.MaximumBytes
            || binding.Revision <= 0
        )
            return TripErrors.Unavailable;
        try
        {
            using var document = JsonDocument.Parse(
                plaintext,
                new JsonDocumentOptions { MaxDepth = 4 }
            );
            var root = document.RootElement;
            if (
                !HasFields(root, "schemaVersion", "links")
                || !root.GetProperty("schemaVersion").TryGetInt32(out var schema)
                || schema != 1
            )
                return TripErrors.Unavailable;
            var items = root.GetProperty("links");
            if (
                items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() > TripFlightLinks.MaximumLinks
            )
                return TripErrors.Unavailable;
            var links = new List<TripFlightLink>();
            foreach (var item in items.EnumerateArray())
            {
                if (
                    !HasFields(item, "orderId", "linkedAt")
                    || !Guid.TryParseExact(
                        item.GetProperty("orderId").GetString(),
                        "D",
                        out var orderId
                    )
                    || !DateTimeOffset.TryParseExact(
                        item.GetProperty("linkedAt").GetString(),
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var linkedAt
                    )
                )
                    return TripErrors.Unavailable;
                links.Add(new(orderId, linkedAt));
            }
            var restored = TripFlightLinks.Restore(binding.TripId, binding.Revision, links);
            return restored.IsError ? TripErrors.Unavailable : restored.Value;
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return TripErrors.Unavailable;
        }
    }

    private static bool HasFields(JsonElement element, string first, string second) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Count() == 2
        && element.TryGetProperty(first, out _)
        && element.TryGetProperty(second, out _);
}
