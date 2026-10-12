using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

/// <summary>Read the offer wrapper once so duplicate informational branches cannot confirm the last value.</summary>
public sealed class DuffelOfferJsonConverter : JsonConverter<DuffelOfferDto>
{
    public override DuffelOfferDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Offer must be an object.");
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var repeated =
            root.EnumerateObject().Count(p => p.Name.Equals("conditions", comparison)) > 1;
        var conditions = repeated
            ? new DuffelConditionsDto(
                new(null, null, null, true),
                new(null, null, null, true),
                true
            )
            : ReadMember<DuffelConditionsDto?>(root, "conditions", options);
        return new(
            ReadMember<string>(root, "id", options)!,
            ReadMember<string>(root, "total_amount", options)!,
            ReadMember<string>(root, "total_currency", options)!,
            ReadMember<DateTimeOffset>(root, "expires_at", options),
            ReadMember<DuffelSliceDto[]>(root, "slices", options)!,
            conditions,
            ReadMember<DuffelOfferPassengerDto[]?>(root, "passengers", options),
            ReadMember<DuffelPaymentRequirementsDto?>(root, "payment_requirements", options),
            ReadMember<bool?>(root, "passenger_identity_documents_required", options),
            ReadMember<DuffelAncillaryServiceDto[]?>(root, "available_services", options),
            ReadMember<JsonElement?>(root, "intended_services", options),
            ReadMember<JsonElement?>(root, "intended_payment_methods", options)
        );
    }

    private static T? ReadMember<T>(JsonElement root, string name, JsonSerializerOptions options)
    {
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var members = root.EnumerateObject().Where(p => p.Name.Equals(name, comparison)).ToArray();
        T? result = default;
        // Preserve ordinary STJ strictness: an earlier malformed mandatory value
        // cannot be hidden by a later valid occurrence.
        foreach (var member in members)
            result = member.Value.Deserialize<T>(options);
        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelOfferDto value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        Member(writer, "id", value.Id, options);
        Member(writer, "total_amount", value.TotalAmount, options);
        Member(writer, "total_currency", value.TotalCurrency, options);
        Member(writer, "expires_at", value.ExpiresAt, options);
        Member(writer, "slices", value.Slices, options);
        Member(writer, "conditions", value.Conditions, options);
        Member(writer, "passengers", value.Passengers, options);
        Member(writer, "payment_requirements", value.PaymentRequirements, options);
        Member(
            writer,
            "passenger_identity_documents_required",
            value.PassengerIdentityDocumentsRequired,
            options
        );
        Member(writer, "available_services", value.AvailableServices, options);
        Member(writer, "intended_services", value.IntendedServices, options);
        Member(writer, "intended_payment_methods", value.IntendedPaymentMethods, options);
        writer.WriteEndObject();
    }

    private static void Member<T>(
        Utf8JsonWriter writer,
        string name,
        T value,
        JsonSerializerOptions options
    )
    {
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, options);
    }
}
