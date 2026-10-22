using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using ErrorOr;

namespace Travel.Modules.Rail.Core;

public sealed record RailStopFact<T>(string State, T? Value, string? Reason)
    where T : struct
{
    public static RailStopFact<T> Reported(T value) => new("reported", value, null);

    public static RailStopFact<T> Missing(string reason) => new("not-provided", null, reason);

    public static RailStopFact<T> Unavailable(string reason) => new("unavailable", null, reason);
}

public sealed record RailTextFact(string State, string? Value, string? Reason)
{
    public static RailTextFact Reported(string value) => new("reported", value, null);

    public static RailTextFact Missing(string reason) => new("not-provided", null, reason);

    public static RailTextFact Unavailable(string reason) => new("unavailable", null, reason);
}

public sealed record RailRouteStation(string Source, string Code, string Name);

public sealed record RailRouteStop(
    int Sequence,
    RailRouteStation Station,
    RailStopFact<DateTimeOffset> Arrival,
    RailStopFact<DateTimeOffset> Departure,
    RailStopFact<long> DwellSeconds,
    RailTextFact Platform,
    ImmutableArray<string> Reasons
);

public sealed record RailRoute(
    ImmutableArray<RailRouteStop> Stops,
    int BoardingIndex,
    int AlightingIndex
);

public static class RailRouteRules
{
    public static ErrorOr<DateTimeOffset> Time(string? value) =>
        value is not null
        && Regex.IsMatch(
            value,
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:Z|[+-]\d{2}:\d{2})$",
            RegexOptions.CultureInvariant
        )
        && DateTimeOffset.TryParseExact(
            value,
            ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed
        )
            ? parsed
            : Error.Validation("Rail.Selection.InvalidTime");

    public static ErrorOr<RailRouteStation> Station(string code, string name) =>
        Regex.IsMatch(code, "^s[0-9]+$", RegexOptions.CultureInvariant)
        && System.Text.Encoding.UTF8.GetByteCount(code) <= 128
        && !string.IsNullOrWhiteSpace(name)
        && RailRules.LabelValid(name)
            ? new RailRouteStation("yandex-rasp", code, name)
            : Error.Validation("Rail.Route.InvalidStation");

    public static bool SameTime(DateTimeOffset a, DateTimeOffset b) =>
        a == b && a.Offset == b.Offset;

    public static ErrorOr<RailRoute> Validate(
        IEnumerable<RailRouteStop> source,
        StationRef from,
        StationRef to,
        DateTimeOffset departure,
        DateTimeOffset arrival
    )
    {
        var stops = source.ToImmutableArray();
        if (
            stops.Length is < 2 or > 200
            || from.Source != "yandex-rasp"
            || to.Source != "yandex-rasp"
        )
            return Error.Validation("Rail.Route.Invalid");
        DateTimeOffset? previous = null;
        var pairs = new List<(int From, int To)>();
        for (var i = 0; i < stops.Length; i++)
        {
            var stop = stops[i];
            if (
                stop.Sequence != i
                || stop.Station.Source != "yandex-rasp"
                || Station(stop.Station.Code, stop.Station.Name).IsError
                || !ValidFact(stop.Arrival)
                || !ValidFact(stop.Departure)
                || !ValidFact(stop.DwellSeconds)
                || (
                    stop.Platform.State == "reported"
                        ? string.IsNullOrEmpty(stop.Platform.Value)
                            || !RailRules.LabelValid(stop.Platform.Value)
                            || stop.Platform.Reason is not null
                        : stop.Platform.State is not ("not-provided" or "unavailable")
                            || stop.Platform.Value is not null
                            || string.IsNullOrEmpty(stop.Platform.Reason)
                )
                || stop.DwellSeconds.Value is < 0 or > 9007199254740991L
            )
                return Error.Validation("Rail.Route.Invalid");
            foreach (var fact in new[] { stop.Arrival, stop.Departure })
                if (fact.Value.HasValue)
                {
                    if (previous.HasValue && fact.Value < previous)
                        return Error.Validation("Rail.Route.InvalidChronology");
                    previous = fact.Value;
                }
            if (
                stop.DwellSeconds.Value.HasValue
                && stop.Arrival.Value.HasValue
                && stop.Departure.Value.HasValue
                && stop.DwellSeconds.Value.Value
                    != (stop.Departure.Value.Value - stop.Arrival.Value.Value).TotalSeconds
            )
                return Error.Validation("Rail.Route.InvalidDwell");
            if (
                stop.Station.Code != from.Code
                || !stop.Departure.Value.HasValue
                || !SameTime(stop.Departure.Value.Value, departure)
            )
                continue;
            for (var j = i + 1; j < stops.Length; j++)
                if (
                    stops[j].Station.Code == to.Code
                    && stops[j].Arrival.Value.HasValue
                    && SameTime(stops[j].Arrival.Value!.Value, arrival)
                )
                    pairs.Add((i, j));
        }
        return pairs.Count == 1
            ? new RailRoute(stops, pairs[0].From, pairs[0].To)
            : Error.Validation("Rail.Route.Mismatch");
    }

    public static RailStopFact<long> Dwell(
        long? seconds,
        DateTimeOffset? arrival,
        DateTimeOffset? departure
    ) =>
        seconds is null ? RailStopFact<long>.Missing("missing-dwell")
        : seconds is < 0 or > 9007199254740991L
        || (
            arrival.HasValue
            && departure.HasValue
            && (departure.Value - arrival.Value).TotalSeconds != seconds
        )
            ? RailStopFact<long>.Unavailable("invalid-dwell")
        : RailStopFact<long>.Reported(seconds.Value);

    private static bool ValidFact<T>(RailStopFact<T> fact)
        where T : struct =>
        fact.State == "reported"
            ? fact.Value.HasValue && fact.Reason is null
            : fact.State is "not-provided" or "unavailable"
                && !fact.Value.HasValue
                && !string.IsNullOrEmpty(fact.Reason);
}
