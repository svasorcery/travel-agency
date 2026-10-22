using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using static Travel.Modules.Rail.Infrastructure.YandexRailScheduleParser;

namespace Travel.Modules.Rail.Infrastructure;

public static class YandexRailRouteParser
{
    public static RailRouteResult Parse(
        ReadOnlyMemory<byte> body,
        ScheduledJourney journey,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (body.Length > 2 * 1024 * 1024)
                return RailRouteResult.Unavailable("failed", "output-limit", receivedAt);
            var bounded = new Utf8JsonReader(body.Span, new JsonReaderOptions { MaxDepth = 64 });
            while (bounded.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    bounded.ValueSpan.Length > 16384
                    || (
                        bounded.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray
                        && bounded.CurrentDepth >= 32
                    )
                )
                    return RailRouteResult.Unavailable("failed", "output-limit", receivedAt);
            }
            using var document = Read(body, 2 * 1024 * 1024);
            var root = document.RootElement;
            if (
                Text(root, "uid") != journey.ThreadReference
                || Text(root, "transport_type") != journey.Transport
            )
                return RailRouteResult.Unavailable("failed", "route-mismatch", receivedAt);
            if (
                One(root, "interval").ValueKind
                is not (JsonValueKind.Undefined or JsonValueKind.Null)
            )
                return RailRouteResult.Unavailable("failed", "unsupported-route-shape", receivedAt);
            foreach (
                var (field, code) in new[] { ("from", journey.From.Code), ("to", journey.To.Code) }
            )
            {
                var endpoint = One(root, field);
                if (
                    endpoint.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                    && (endpoint.ValueKind != JsonValueKind.String || endpoint.GetString() != code)
                )
                    return RailRouteResult.Unavailable("failed", "route-mismatch", receivedAt);
            }
            var reasons = ImmutableArray.CreateBuilder<string>();
            foreach (
                var (field, expected) in new[]
                {
                    ("departure_date", journey.Departure),
                    ("arrival_date", journey.Arrival),
                }
            )
            {
                var echo = One(root, field);
                if (echo.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                var value = echo.ValueKind == JsonValueKind.String ? echo.GetString() : null;
                DateOnly? calendar = null;
                if (
                    value is not null
                    && DateOnly.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var day
                    )
                )
                    calendar = day;
                else if (!RailRouteRules.Time(value).IsError)
                    calendar = DateOnly.FromDateTime(RailRouteRules.Time(value).Value.Date);
                if (calendar.HasValue && calendar.Value != DateOnly.FromDateTime(expected.Date))
                    return RailRouteResult.Unavailable("failed", "route-mismatch", receivedAt);
                if (!calendar.HasValue)
                    reasons.Add("echo-date-unavailable");
            }
            var rows = One(root, "stops");
            if (rows.ValueKind != JsonValueKind.Array)
                throw new JsonException();
            if (rows.GetArrayLength() is < 2 or > 200)
                return RailRouteResult.Unavailable("failed", "output-limit", receivedAt);
            var stops = ImmutableArray.CreateBuilder<RailRouteStop>();
            foreach (var row in rows.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var station = One(row, "station");
                var type = One(station, "type");
                if (type.ValueKind != JsonValueKind.Undefined && type.GetString() != "station")
                    throw new JsonException();
                var identity = RailRouteRules.Station(
                    Text(station, "code"),
                    Text(station, "title")
                );
                if (identity.IsError)
                    throw new JsonException();
                var local = ImmutableArray.CreateBuilder<string>();
                var arrival = TimeFact(row, "arrival", stops.Count == 0, local);
                var departure = TimeFact(
                    row,
                    "departure",
                    stops.Count == rows.GetArrayLength() - 1,
                    local
                );
                RailStopFact<long> dwell;
                try
                {
                    var value = One(row, "stop_time");
                    if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                        dwell = RailRouteRules.Dwell(null, arrival.Value, departure.Value);
                    else if (
                        !ExactInteger(value, out var number) || number is < 0 or > 9007199254740991L
                    )
                        dwell = RailStopFact<long>.Unavailable("invalid-dwell");
                    else
                        dwell = RailRouteRules.Dwell((long)number, arrival.Value, departure.Value);
                }
                catch (JsonException)
                {
                    dwell = RailStopFact<long>.Unavailable("invalid-dwell");
                }
                if (dwell.State == "unavailable")
                    local.Add("invalid-dwell");
                RailTextFact platform;
                try
                {
                    var value = One(row, "platform");
                    platform =
                        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                        || (value.ValueKind == JsonValueKind.String && value.GetString() == "")
                            ? RailTextFact.Missing("missing-platform")
                        : value.ValueKind == JsonValueKind.String
                        && RailRules.LabelValid(value.GetString()!)
                            ? RailTextFact.Reported(value.GetString()!)
                        : RailTextFact.Unavailable("invalid-platform");
                }
                catch (JsonException)
                {
                    platform = RailTextFact.Unavailable("invalid-platform");
                }
                if (platform.State == "unavailable")
                    local.Add("invalid-platform");
                reasons.AddRange(local);
                stops.Add(
                    new(
                        stops.Count,
                        identity.Value,
                        arrival,
                        departure,
                        dwell,
                        platform,
                        local.Distinct().ToImmutableArray()
                    )
                );
            }
            var route = RailRouteRules.Validate(
                stops,
                journey.From,
                journey.To,
                journey.Departure,
                journey.Arrival
            );
            if (route.IsError)
                return RailRouteResult.Unavailable("failed", "route-mismatch", receivedAt);
            cancellationToken.ThrowIfCancellationRequested();
            return new(
                reasons.Count == 0 ? "complete" : "limited",
                reasons.Distinct().ToImmutableArray(),
                route.Value.Stops,
                route.Value.BoardingIndex,
                route.Value.AlightingIndex,
                receivedAt
            );
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or FormatException
                        or OverflowException
            )
        {
            return RailRouteResult.Unavailable("failed", "mapping", receivedAt);
        }
    }

    private static RailStopFact<DateTimeOffset> TimeFact(
        JsonElement row,
        string name,
        bool terminal,
        ImmutableArray<string>.Builder reasons
    )
    {
        try
        {
            var value = One(row, name);
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (!terminal)
                    reasons.Add("missing-time");
                return RailStopFact<DateTimeOffset>.Missing(
                    terminal ? "terminal-not-provided" : "missing-time"
                );
            }
            var time = RailRouteRules.Time(
                value.ValueKind == JsonValueKind.String ? value.GetString() : null
            );
            if (!time.IsError)
                return RailStopFact<DateTimeOffset>.Reported(time.Value);
        }
        catch (JsonException)
        { /* Optional duplicate is a local defect; endpoint matching remains mandatory. */
        }
        reasons.Add("invalid-time");
        return RailStopFact<DateTimeOffset>.Unavailable("invalid-time");
    }
}
