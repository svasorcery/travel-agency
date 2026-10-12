using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

// Only optional informational fields use this reader. Required offer fields retain
// their normal System.Text.Json parsing and existing provider validation.
internal static class DuffelFareJson
{
    internal static bool Provided(JsonElement value) =>
        value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    internal static bool InvalidObject(JsonElement value) =>
        Provided(value) && (value.ValueKind != JsonValueKind.Object || !IsBounded(value));

    internal static bool InvalidMember(JsonElement value, string name)
    {
        if (InvalidObject(value))
            return true;
        return value.ValueKind == JsonValueKind.Object
            && value.EnumerateObject().Count(x => x.NameEquals(name)) > 1;
    }

    internal static bool InvalidLabel(JsonElement value, int maximum) =>
        Provided(value) && Label(value, maximum) is null;

    internal static bool IsBounded(JsonElement value) =>
        System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) <= 128 * 1024;

    internal static JsonElement Member(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !IsBounded(value))
            return default;
        var result = default(JsonElement);
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (property.NameEquals(name))
            {
                result = property.Value;
                count++;
            }
        }
        return count == 1 ? result : default;
    }

    internal static string? Label(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return
            text is { Length: > 0 }
            && text.Length <= maximum
            && !text.Any(c =>
                char.IsControl(c)
                || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format
            )
            ? text
            : null;
    }

    internal static DuffelConditionDto Condition(JsonElement value)
    {
        var allowed = Member(value, "allowed");
        var amount = Member(value, "penalty_amount");
        var currency = Member(value, "penalty_currency");
        return new DuffelConditionDto(
            allowed.ValueKind == JsonValueKind.True ? true
                : allowed.ValueKind == JsonValueKind.False ? false
                : null,
            Label(amount, 64),
            Label(currency, 3),
            InvalidObject(value)
                || InvalidMember(value, "allowed")
                || InvalidMember(value, "penalty_amount")
                || InvalidMember(value, "penalty_currency")
                || (
                    Provided(allowed)
                    && allowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                )
                || InvalidLabel(amount, 64)
                || InvalidLabel(currency, 3)
        );
    }

    internal static DuffelBaggageDto Baggage(JsonElement value)
    {
        var quantity = Member(value, "quantity");
        var type = Label(Member(value, "type"), 64);
        int? parsedQuantity =
            quantity.ValueKind == JsonValueKind.Number
            && quantity.TryGetInt32(out var count)
            && count >= 0
                ? count
                : null;
        return new DuffelBaggageDto(
            type is "checked" or "carry_on" ? type : null,
            parsedQuantity,
            InvalidObject(value)
                || InvalidMember(value, "type")
                || InvalidMember(value, "quantity")
                || (Provided(Member(value, "type")) && type is not ("checked" or "carry_on"))
                || (Provided(quantity) && parsedQuantity is null)
        );
    }

    internal static DuffelBaggageDto[]? Baggages(JsonElement value)
    {
        if (
            value.ValueKind != JsonValueKind.Array
            || !IsBounded(value)
            || value.GetArrayLength() > 128
        )
            return null;
        var items = value
            .EnumerateArray()
            .Select(item =>
                item.ValueKind == JsonValueKind.Object
                    ? Baggage(item)
                    : new DuffelBaggageDto(null, null, true)
            )
            .ToArray();
        var duplicates = items
            .Where(x => x.Type is not null)
            .GroupBy(x => x.Type)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToHashSet();
        return items
            .Select(x =>
                duplicates.Contains(x.Type) ? x with { Quantity = null, InvalidData = true } : x
            )
            .ToArray();
    }
}

public sealed class DuffelConditionJsonConverter : JsonConverter<DuffelConditionDto>
{
    public override DuffelConditionDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return DuffelFareJson.Condition(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelConditionDto value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WritePropertyName("allowed");
        if (value.Allowed is { } allowed)
            writer.WriteBooleanValue(allowed);
        else
            writer.WriteNullValue();
        writer.WriteString("penalty_amount", value.PenaltyAmount);
        writer.WriteString("penalty_currency", value.PenaltyCurrency);
        writer.WriteEndObject();
    }
}

public sealed class DuffelConditionsJsonConverter : JsonConverter<DuffelConditionsDto>
{
    public override DuffelConditionsDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        var change = DuffelFareJson.Condition(
            DuffelFareJson.Member(value, "change_before_departure")
        );
        var refund = DuffelFareJson.Condition(
            DuffelFareJson.Member(value, "refund_before_departure")
        );
        change = change with
        {
            InvalidData =
                change.InvalidData
                || DuffelFareJson.InvalidMember(value, "change_before_departure"),
        };
        refund = refund with
        {
            InvalidData =
                refund.InvalidData
                || DuffelFareJson.InvalidMember(value, "refund_before_departure"),
        };
        return new DuffelConditionsDto(
            change,
            refund,
            DuffelFareJson.InvalidObject(value) || change.InvalidData || refund.InvalidData
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelConditionsDto value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WritePropertyName("change_before_departure");
        JsonSerializer.Serialize(writer, value.ChangeBeforeDeparture, options);
        writer.WritePropertyName("refund_before_departure");
        JsonSerializer.Serialize(writer, value.RefundBeforeDeparture, options);
        writer.WriteEndObject();
    }
}

public sealed class DuffelBaggageJsonConverter : JsonConverter<DuffelBaggageDto>
{
    public override DuffelBaggageDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return DuffelFareJson.Baggage(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelBaggageDto value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WritePropertyName("quantity");
        if (value.Quantity is { } quantity)
            writer.WriteNumberValue(quantity);
        else
            writer.WriteNullValue();
        writer.WriteEndObject();
    }
}

public sealed class DuffelIncludedBaggageJsonConverter : JsonConverter<DuffelBaggageDto[]>
{
    public override DuffelBaggageDto[]? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return DuffelFareJson.Baggages(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelBaggageDto[] value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();
        foreach (var baggage in value)
            JsonSerializer.Serialize(writer, baggage, options);
        writer.WriteEndArray();
    }
}

public class DuffelFareLabelJsonConverter : JsonConverter<string>
{
    protected virtual int MaximumLength => 256;

    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return DuffelFareJson.Label(document.RootElement, MaximumLength);
    }

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value);
}

public sealed class DuffelFareCodeJsonConverter : DuffelFareLabelJsonConverter
{
    protected override int MaximumLength => 64;
}

public sealed class DuffelFareSliceJsonConverter : JsonConverter<DuffelSliceDto>
{
    public override DuffelSliceDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a Duffel slice object.");
        // Deserialize every occurrence of required fields so malformed mandatory
        // data cannot be hidden by a later duplicate member.
        DuffelSegmentDto[]? segments = null;
        string? id = null;
        foreach (var property in value.EnumerateObject())
        {
            if (property.NameEquals("segments"))
                segments = property.Value.Deserialize<DuffelSegmentDto[]>(options);
            if (property.NameEquals("id"))
                id = property.Value.Deserialize<string>(options);
        }
        var conditions = DuffelFareJson.Member(value, "conditions");
        var brand = DuffelFareJson.Member(value, "fare_brand_name");
        var parsedConditions = conditions.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : conditions.Deserialize<DuffelConditionsDto>(options);
        return new DuffelSliceDto(
            segments!,
            DuffelFareJson.Label(brand, 256),
            id,
            parsedConditions,
            DuffelFareJson.InvalidMember(value, "fare_brand_name")
                || DuffelFareJson.InvalidLabel(brand, 256)
                || DuffelFareJson.InvalidMember(value, "conditions")
                || parsedConditions?.InvalidData == true
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelSliceDto value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WritePropertyName("segments");
        JsonSerializer.Serialize(writer, value.Segments, options);
        writer.WriteString("fare_brand_name", value.FareBrandName);
        writer.WriteString("id", value.Id);
        writer.WritePropertyName("conditions");
        JsonSerializer.Serialize(writer, value.Conditions, options);
        writer.WriteEndObject();
    }
}

public sealed class DuffelFarePassengerJsonConverter : JsonConverter<DuffelSegmentPassengerDto>
{
    public override DuffelSegmentPassengerDto Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a Duffel passenger object.");
        string? cabin = null;
        string? passengerId = null;
        foreach (var property in value.EnumerateObject())
        {
            if (property.NameEquals("cabin_class"))
                cabin = property.Value.Deserialize<string>(options);
            if (property.NameEquals("passenger_id"))
                passengerId = property.Value.Deserialize<string>(options);
        }
        var marketing = DuffelFareJson.Member(value, "cabin_class_marketing_name");
        var code = DuffelFareJson.Member(value, "fare_basis_code");
        var baggageValue = DuffelFareJson.Member(value, "baggages");
        var baggages = DuffelFareJson.Baggages(baggageValue);
        return new DuffelSegmentPassengerDto(
            cabin!,
            DuffelFareJson.Label(marketing, 256),
            baggages,
            passengerId,
            DuffelFareJson.Label(code, 64),
            DuffelFareJson.InvalidMember(value, "cabin_class_marketing_name")
                || DuffelFareJson.InvalidLabel(marketing, 256)
                || DuffelFareJson.InvalidMember(value, "fare_basis_code")
                || DuffelFareJson.InvalidLabel(code, 64)
                || DuffelFareJson.InvalidMember(value, "baggages")
                || (DuffelFareJson.Provided(baggageValue) && baggages is null)
                || baggages?.Any(x => x.InvalidData) == true
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        DuffelSegmentPassengerDto value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WriteString("cabin_class", value.CabinClass);
        writer.WriteString("cabin_class_marketing_name", value.CabinClassMarketingName);
        writer.WritePropertyName("baggages");
        JsonSerializer.Serialize(writer, value.Baggages, options);
        writer.WriteString("passenger_id", value.PassengerId);
        writer.WriteString("fare_basis_code", value.FareBasisCode);
        writer.WriteEndObject();
    }
}
