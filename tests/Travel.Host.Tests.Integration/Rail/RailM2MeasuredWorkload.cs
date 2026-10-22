using System.Globalization;
using System.Text.Json;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;

namespace Travel.Host.Tests.Integration.Rail;

// Source-only preparation. This helper has no fixture/process/load side effects.
public static class RailM2MeasuredWorkload
{
    public static RailJourneyDetailsRequest Input(
        RailRequest query,
        string epoch,
        DateTimeOffset now
    )
    {
        var departure = RailRouteRules.Time(query.DepartureDate + "T10:00:00+03:00").Value;
        var arrival = RailRouteRules.Time(query.DepartureDate + "T12:00:00+03:00").Value;
        if (now.ToUniversalTime().Date < departure.ToUniversalTime().Date.AddDays(-30))
            throw new InvalidOperationException("Fixture date outside measured window");
        var key = string.Join(
            "|",
            "yandex-rasp",
            "measured-train",
            "train",
            query.From.Code,
            query.To.Code,
            departure.UtcTicks.ToString(CultureInfo.InvariantCulture),
            arrival.UtcTicks.ToString(CultureInfo.InvariantCulture)
        );
        return new(query, epoch, new(key, "train", departure, arrival));
    }

    public static object Thread(string from, string to, string date, string uid) =>
        new
        {
            uid,
            transport_type = "train",
            departure_date = date,
            arrival_date = date,
            stops = Enumerable
                .Range(0, 200)
                .Select(index => new
                {
                    station = new
                    {
                        code = index == 0 ? from
                        : index == 199 ? to
                        : "s900003",
                        title = "Учебная " + new string('я', 128),
                        type = "station",
                    },
                    arrival = index == 0 ? null
                    : index == 199 ? date + "T12:00:00+03:00"
                    : RailRouteRules
                        .Time(date + "T10:00:00+03:00")
                        .Value.AddSeconds(index)
                        .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                    departure = index == 199
                        ? null
                        : RailRouteRules
                            .Time(date + "T10:00:00+03:00")
                            .Value.AddSeconds(index)
                            .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                    stop_time = index is 0 or 199 ? (int?)null : 0,
                    platform = "Учебная платформа",
                })
                .ToArray(),
        };

    public static bool UsefulSearch(
        int status,
        JsonElement body,
        RailRequest query,
        string expectedEpoch
    )
    {
        try
        {
            if (
                status != 200
                || body.GetProperty("kind").GetString() != "complete"
                || body.GetProperty("items").GetArrayLength() != 2
                || !Authority(body, expectedEpoch)
                || !JsonElement.DeepEquals(
                    body.GetProperty("request"),
                    JsonSerializer.SerializeToElement(query, JsonSerializerOptions.Web)
                )
                || body.GetProperty("normalizedCount").GetInt32() != 2
                || body.GetProperty("modes").GetArrayLength() != 2
                || body.GetProperty("modes")
                    .EnumerateArray()
                    .Any(mode => mode.GetProperty("kind").GetString() != "complete")
            )
                return false;
            var modes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in body.GetProperty("items").EnumerateArray())
            {
                if (
                    row.GetProperty("from").GetProperty("code").GetString() != query.From.Code
                    || row.GetProperty("to").GetProperty("code").GetString() != query.To.Code
                    || !modes.Add(row.GetProperty("transport").GetString()!)
                    || row.GetProperty("departure").GetString()
                        != query.DepartureDate + "T10:00:00+03:00"
                    || row.GetProperty("arrival").GetString()
                        != query.DepartureDate + "T12:00:00+03:00"
                    || row.GetProperty("durationSeconds").GetInt64() != 7200
                )
                    return false;
            }
            return modes.SetEquals(["train", "suburban"]);
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
            )
        {
            return false;
        }
    }

    public static bool UsefulDetail(
        int status,
        JsonElement body,
        RailJourneyDetailsRequest expected
    )
    {
        try
        {
            var query = expected.Search;
            if (
                status != 200
                || body.GetProperty("kind").GetString() != "complete"
                || body.GetProperty("reasons").GetArrayLength() != 0
                || !Authority(body, expected.ExpectedProfileEpoch)
                || body.GetProperty("stage").GetString() != "route"
                || !JsonElement.DeepEquals(
                    body.GetProperty("request"),
                    JsonSerializer.SerializeToElement(expected, JsonSerializerOptions.Web)
                )
                || body.GetProperty("verification").ValueKind != JsonValueKind.Object
                || body.GetProperty("stops").GetArrayLength() != 200
                || body.GetProperty("boardingIndex").GetInt32() != 0
                || body.GetProperty("alightingIndex").GetInt32() != 199
            )
                return false;
            var verification = body.GetProperty("verification");
            var received = body.GetProperty("routeReceivedAt").GetDateTimeOffset();
            var completed = body.GetProperty("completedAt").GetDateTimeOffset();
            var expiry = body.GetProperty("expiresAt").GetDateTimeOffset();
            if (
                verification.GetProperty("transport").GetString() != expected.Selection.Transport
                || verification.GetProperty("kind").GetString() != "complete"
                || verification.GetProperty("receivedAt").GetDateTimeOffset() > received
                || received > completed
                || completed >= expiry
                || expiry > received.AddSeconds(60)
                || body.GetProperty("attribution").ValueKind != JsonValueKind.Object
            )
                return false;
            var stops = JsonSerializer.Deserialize<RailRouteStop[]>(
                body.GetProperty("stops"),
                JsonSerializerOptions.Web
            )!;
            var departure = RailRouteRules.Time(query.DepartureDate + "T10:00:00+03:00").Value;
            var arrival = RailRouteRules.Time(query.DepartureDate + "T12:00:00+03:00").Value;
            var route = RailRouteRules.Validate(stops, query.From, query.To, departure, arrival);
            return !route.IsError
                && stops.All(s =>
                    s.Reasons.IsEmpty
                    && s.Platform.State != "unavailable"
                    && s.DwellSeconds.State != "unavailable"
                    && (
                        s.Sequence == 0
                            ? s.Arrival.Reason == "terminal-not-provided"
                            : s.Arrival.State == "reported"
                    )
                    && (
                        s.Sequence == 199
                            ? s.Departure.Reason == "terminal-not-provided"
                            : s.Departure.State == "reported"
                    )
                );
        }
        catch (Exception error)
            when (error
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    private static bool Authority(JsonElement body, string expectedEpoch) =>
        body.GetProperty("source").GetString() == "yandex-rasp"
        && body.GetProperty("profile").GetString() == "fictional"
        && body.GetProperty("profileEpoch").GetString() == expectedEpoch;
}
