using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Infrastructure;

public static class YandexRailScheduleParser
{
    public static ModeResult Parse(
        ReadOnlyMemory<byte> body,
        RailRequest request,
        string mode,
        DateTimeOffset receivedAt
    )
    {
        try
        {
            using var document = Read(body, 2 * 1024 * 1024);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            if (!MatchesEcho(root, request))
                return ModeResult.Unavailable(mode, "failed", "query-mismatch");
            var rows = One(root, "segments");
            var page = One(root, "pagination");
            var total = Integer(page, "total");
            if (
                rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() > 50
                || total < rows.GetArrayLength()
                || Integer(page, "offset") != 0
                || Integer(page, "limit") != 50
            )
                throw new JsonException();
            var reasons = ImmutableArray.CreateBuilder<string>();
            var intervals = One(root, "interval_segments");
            if (intervals.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Array))
                throw new JsonException();
            if (intervals.ValueKind == JsonValueKind.Array && intervals.GetArrayLength() > 0)
            {
                foreach (var row in intervals.EnumerateArray())
                    CheckRoute(row, request, mode);
                reasons.Add("interval-service-unsupported");
            }
            else if (total > rows.GetArrayLength())
                reasons.Add("page-limit");
            var unique = new Dictionary<string, ScheduledJourney>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                CheckRoute(row, request, mode);
                var thread = One(row, "thread");
                var uid = Text(thread, "uid");
                if (string.IsNullOrWhiteSpace(uid) || uid.Length > 100)
                    throw new JsonException();
                var departure = ParseTime(Text(row, "departure"));
                var arrival = ParseTime(Text(row, "arrival"));
                var duration = One(row, "duration");
                if (
                    duration.ValueKind != JsonValueKind.Number
                    || !ExactInteger(duration, out var seconds)
                    || seconds <= 0
                    || arrival <= departure
                    || (decimal)(arrival - departure).TotalSeconds != seconds
                    || departure.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        != request.DepartureDate
                )
                    throw new JsonException();
                var metadata = ImmutableArray.CreateBuilder<string>();
                var sourceDate = TextOptional(row, "start_date", metadata);
                if (
                    sourceDate is not null
                    && !DateOnly.TryParseExact(
                        sourceDate,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out _
                    )
                )
                {
                    sourceDate = null;
                    metadata.Add("invalid-source-date");
                }
                var key = string.Join(
                    "|",
                    "yandex-rasp",
                    uid,
                    mode,
                    request.From.Code,
                    request.To.Code,
                    departure.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    arrival.UtcTicks.ToString(CultureInfo.InvariantCulture)
                );
                var journey = new ScheduledJourney(
                    key,
                    uid,
                    mode,
                    request.From,
                    request.To,
                    departure,
                    arrival,
                    seconds,
                    sourceDate,
                    TextOptional(thread, "number", metadata),
                    TextOptional(thread, "title", metadata),
                    OptionalCarrier(thread, metadata),
                    TextOptional(row, "departure_platform", metadata),
                    TextOptional(row, "arrival_platform", metadata),
                    TextOptional(row, "stops", metadata),
                    TicketBlock(row, receivedAt),
                    receivedAt,
                    metadata.ToImmutable()
                );
                if (RailRules.Journey(journey, request).IsError)
                    throw new JsonException();
                if (unique.TryGetValue(key, out var previous))
                {
                    if (JsonSerializer.Serialize(previous) != JsonSerializer.Serialize(journey))
                        throw new JsonException();
                }
                else
                    unique.Add(key, journey);
            }
            return new ModeResult(
                mode,
                reasons.Count == 0 ? "complete" : "limited",
                reasons.ToImmutable(),
                rows.GetArrayLength(),
                unique.Values.ToImmutableArray(),
                receivedAt
            );
        }
        catch (JsonException)
        {
            return ModeResult.Unavailable(mode, "failed", "mapping");
        }
        catch (FormatException)
        {
            return ModeResult.Unavailable(mode, "failed", "mapping");
        }
        catch (InvalidOperationException)
        {
            return ModeResult.Unavailable(mode, "failed", "mapping");
        }
    }

    private static bool MatchesEcho(JsonElement root, RailRequest request)
    {
        try
        {
            var echo = One(root, "search");
            return Endpoint(One(echo, "from"), request.From.Code)
                && Endpoint(One(echo, "to"), request.To.Code)
                && Text(echo, "date") == request.DepartureDate;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static JsonDocument Read(ReadOnlyMemory<byte> body, int limit)
    {
        if (body.Length > limit)
            throw new JsonException();
        var reader = new Utf8JsonReader(body.Span, new JsonReaderOptions { MaxDepth = 32 });
        while (reader.Read())
            if (reader.ValueSpan.Length > 16384)
                throw new JsonException();
        return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
    }

    internal static JsonElement One(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException();
        JsonElement found = default;
        var count = 0;
        foreach (var property in value.EnumerateObject())
            if (property.NameEquals(name))
            {
                found = property.Value;
                count++;
            }
        if (count > 1)
            throw new JsonException();
        return found;
    }

    internal static string Text(JsonElement value, string name) =>
        One(value, name).GetString() ?? throw new JsonException();

    private static int Integer(JsonElement value, string name)
    {
        var element = One(value, name);
        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var result)
            || result < 0
        )
            throw new JsonException();
        return result;
    }

    private static bool Endpoint(JsonElement value, string code) =>
        value.ValueKind == JsonValueKind.Object
        && Text(value, "type") == "station"
        && Text(value, "code") == code;

    private static void CheckRoute(JsonElement row, RailRequest request, string mode)
    {
        if (
            !Endpoint(One(row, "from"), request.From.Code)
            || !Endpoint(One(row, "to"), request.To.Code)
            || Text(One(row, "thread"), "transport_type") != mode
            || One(row, "has_transfers").ValueKind != JsonValueKind.False
        )
            throw new JsonException();
    }

    internal static DateTimeOffset ParseTime(string value)
    {
        if (
            !DateTimeOffset.TryParseExact(
                value,
                ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var time
            )
        )
            throw new JsonException();
        return time;
    }

    private static string? TextOptional(
        JsonElement value,
        string name,
        ImmutableArray<string>.Builder reasons
    )
    {
        try
        {
            var element = One(value, name);
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;
            var text = element.GetString();
            if (text is null || !RailRules.LabelValid(text))
                throw new JsonException();
            return text;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            reasons.Add("optional-label-unavailable");
            return null;
        }
    }

    private static string? OptionalCarrier(
        JsonElement thread,
        ImmutableArray<string>.Builder reasons
    )
    {
        try
        {
            var carrier = One(thread, "carrier");
            if (
                carrier.ValueKind
                is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
            )
                reasons.Add("optional-label-unavailable");
            return carrier.ValueKind == JsonValueKind.Object
                ? TextOptional(carrier, "title", reasons)
                : null;
        }
        catch (JsonException)
        {
            reasons.Add("optional-label-unavailable");
            return null;
        }
    }

    private static TicketInformation TicketBlock(JsonElement row, DateTimeOffset receivedAt)
    {
        try
        {
            return Tickets(One(row, "tickets_info"), receivedAt);
        }
        catch (JsonException)
        {
            return new("unknown", "invalid-indicator", "unavailable", [], ["invalid-tickets"]);
        }
    }

    internal static TicketInformation Tickets(JsonElement value, DateTimeOffset receivedAt)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        var marker = "unknown";
        string? markerReason = "not-provided";
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(marker, markerReason, "not-provided", [], []);
        if (value.ValueKind != JsonValueKind.Object)
            return new(marker, "invalid-indicator", "unavailable", [], ["invalid-tickets"]);
        try
        {
            var indicator = One(value, "et_marker");
            if (indicator.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                marker = indicator.GetBoolean() ? "true" : "false";
                markerReason = null;
            }
            else if (indicator.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                markerReason = "invalid-indicator";
        }
        catch (JsonException)
        {
            markerReason = "invalid-indicator";
        }
        JsonElement places;
        try
        {
            places = One(value, "places");
        }
        catch (JsonException)
        {
            return new(marker, markerReason, "unavailable", [], ["invalid-fares"]);
        }
        if (places.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(marker, markerReason, "not-provided", [], []);
        if (places.ValueKind != JsonValueKind.Array)
            return new(marker, markerReason, "unavailable", [], ["invalid-fares"]);
        var fares = ImmutableArray.CreateBuilder<RailFare>();
        foreach (var fare in places.EnumerateArray().Take(32))
        {
            var fareReasons = ImmutableArray.CreateBuilder<string>();
            var name = TextOptional(fare, "name", fareReasons);
            RailPrice price;
            try
            {
                var amount = One(fare, "price");
                if (amount.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    price = new("not-provided", null, null, "incomplete-price", receivedAt);
                else
                {
                    var whole = One(amount, "whole");
                    var cents = One(amount, "cents");
                    if (
                        whole.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                        || cents.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    )
                        price = new("not-provided", null, null, "incomplete-price", receivedAt);
                    else
                    {
                        if (!ExactInteger(whole, out var w) || !ExactInteger(cents, out var c))
                            throw new JsonException();
                        var currency = Text(fare, "currency");
                        var money = RailRules.Money(w, c, currency);
                        if (currency.Length != 3 || currency.Any(x => x is < 'A' or > 'Z'))
                            throw new JsonException();
                        price = money.IsError
                            ? new(
                                "unavailable",
                                null,
                                null,
                                currency is "RUB" or "USD" or "EUR"
                                    ? "invalid-price"
                                    : "unsupported-currency",
                                receivedAt
                            )
                            : new("reported", money.Value, currency, null, receivedAt);
                    }
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                price = new("unavailable", null, null, "invalid-price", receivedAt);
            }
            if (price.State == "unavailable" || fareReasons.Count > 0)
                reasons.Add("invalid-fare");
            fares.Add(new(name, price));
        }
        if (places.GetArrayLength() > 32)
            reasons.Add("output-limit");
        return new(
            marker,
            markerReason,
            reasons.Count > 0 ? "limited"
                : fares.Count == 0 ? "not-provided"
                : "reported",
            fares.ToImmutable(),
            reasons.Distinct().ToImmutableArray()
        );
    }

    // Validate mathematical integrality BEFORE decimal parsing, which may round
    // tiny/excess-precision JSON fractions to zero or an integer.
    private static bool ExactInteger(JsonElement element, out decimal number)
    {
        number = 0;
        if (element.ValueKind != JsonValueKind.Number)
            return false;
        var raw = element.GetRawText();
        var parts = raw.Split(['e', 'E']);
        var exponent = 0;
        if (
            parts.Length > 1
            && !int.TryParse(
                parts[1],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out exponent
            )
        )
            return false;
        var negative = parts[0].StartsWith('-');
        var mantissa = negative ? parts[0][1..] : parts[0];
        var dot = mantissa.IndexOf('.');
        long scale = (dot < 0 ? 0 : mantissa.Length - dot - 1) - (long)exponent;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0)
            return true;
        if (scale > 0)
        {
            if (
                scale >= digits.Length
                || digits.AsSpan(digits.Length - (int)scale).IndexOfAnyExcept('0') >= 0
            )
                return false;
            digits = digits[..(digits.Length - (int)scale)];
        }
        else if (scale < 0)
        {
            if (digits.Length - scale > 29)
                return false;
            digits += new string('0', (int)-scale);
        }
        return digits.Length <= 29
            && decimal.TryParse(
                (negative ? "-" : "") + digits,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out number
            );
    }
}
