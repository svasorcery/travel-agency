using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

// Duffel uses origin/destination-local ISO timestamps. An absent offset must not use the host timezone.
public sealed class DuffelLocalTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var raw = reader.GetString();
        if (
            raw is null
            || raw.Length < 19
            || raw[10] != 'T'
            || !DateOnly.TryParseExact(
                raw[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _
            )
            || !DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var timestamp
            )
        )
            throw new JsonException("Supplier timestamp is invalid.");
        return timestamp;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value);
}
