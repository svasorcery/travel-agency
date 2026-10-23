using Shouldly;
using Travel.Modules.Rail.Core;
using Xunit;

namespace Travel.Modules.Rail.Tests.Unit;

public sealed class RailRouteRulesTests
{
    private static readonly StationRef A = StationRef.Create("yandex-rasp", "s1").Value;
    private static readonly StationRef B = StationRef.Create("yandex-rasp", "s2").Value;
    private static readonly DateTimeOffset Departure = DateTimeOffset.Parse(
        "2026-11-01T21:00:00+03:00"
    );
    private static readonly DateTimeOffset Arrival = DateTimeOffset.Parse(
        "2026-11-02T05:00:00+03:00"
    );

    [Fact]
    public void Repeated_stations_select_the_unique_exact_occurrence_and_keep_terminal_nulls()
    {
        var stops = new[]
        {
            Stop(0, A, null, Departure.AddDays(-1)),
            Stop(1, B, Departure.AddHours(-10), Departure.AddHours(-9)),
            Stop(2, A, Departure.AddMinutes(-1), Departure),
            Stop(3, B, Arrival, null),
        };
        var route = RailRouteRules.Validate(stops, A, B, Departure, Arrival);
        route.IsError.ShouldBeFalse();
        route.Value.BoardingIndex.ShouldBe(2);
        route.Value.AlightingIndex.ShouldBe(3);
        route.Value.Stops[0].Arrival.State.ShouldBe("not-provided");
        route.Value.Stops[3].Departure.Reason.ShouldBe("terminal-not-provided");
    }

    [Fact]
    public void Same_instant_with_different_offset_does_not_confirm_selection()
    {
        var stops = new[]
        {
            Stop(0, A, null, Departure.ToOffset(TimeSpan.Zero)),
            Stop(1, B, Arrival, null),
        };
        RailRouteRules.Validate(stops, A, B, Departure, Arrival).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Wrong_chronology_and_missing_selected_time_fail_closed()
    {
        RailRouteRules
            .Validate([Stop(0, A, null, null), Stop(1, B, Arrival, null)], A, B, Departure, Arrival)
            .IsError.ShouldBeTrue();
        RailRouteRules
            .Validate(
                [
                    Stop(0, A, null, Departure),
                    Stop(1, A, Departure.AddHours(-1), Departure),
                    Stop(2, B, Arrival, null),
                ],
                A,
                B,
                Departure,
                Arrival
            )
            .IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-11-01T21:00:00+03:00", true)]
    [InlineData("2026-11-01T21:00:00Z", true)]
    [InlineData("2026-11-01T21:00:00.1+03:00", false)]
    [InlineData("2026-11-01T21:00:00", false)]
    [InlineData("2026-02-29T21:00:00+03:00", false)]
    [InlineData("2026-11-01T24:00:00+03:00", false)]
    [InlineData("2026-11-01T21:00:00+14:01", false)]
    public void Scheduled_times_require_valid_seconds_and_explicit_offset(
        string value,
        bool valid
    ) => RailRouteRules.Time(value).IsError.ShouldBe(!valid);

    [Fact]
    public void Invalid_identity_and_dwell_are_rejected_without_converting_unknown_to_zero()
    {
        RailRouteRules.Station("city1", "Label").IsError.ShouldBeTrue();
        RailRouteRules.Station("s1", "").IsError.ShouldBeTrue();
        RailStopFact<long>.Reported(0).Value.ShouldBe(0);
        RailStopFact<long>.Missing("missing-dwell").Value.ShouldBeNull();
        var stops = new[] { Stop(0, A, null, Departure), Stop(1, B, Arrival, null) };
        stops[0] = stops[0] with { DwellSeconds = RailStopFact<long>.Reported(-1) };
        RailRouteRules.Validate(stops, A, B, Departure, Arrival).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Inconsistent_platform_or_foreign_endpoint_source_cannot_enter_normalized_route()
    {
        var stops = new[] { Stop(0, A, null, Departure), Stop(1, B, Arrival, null) };
        stops[0] = stops[0] with { Platform = new("reported", null, "invalid-platform") };
        RailRouteRules.Validate(stops, A, B, Departure, Arrival).IsError.ShouldBeTrue();
        stops[0] = Stop(0, A, null, Departure);
        RailRouteRules
            .Validate(stops, StationRef.Create("other", "s1").Value, B, Departure, Arrival)
            .IsError.ShouldBeTrue();
    }

    private static RailRouteStop Stop(
        int sequence,
        StationRef station,
        DateTimeOffset? arrival,
        DateTimeOffset? departure
    ) =>
        new(
            sequence,
            new(station.Source, station.Code, "Station " + station.Code),
            arrival.HasValue
                ? RailStopFact<DateTimeOffset>.Reported(arrival.Value)
                : RailStopFact<DateTimeOffset>.Missing(
                    sequence == 0 ? "terminal-not-provided" : "missing-time"
                ),
            departure.HasValue
                ? RailStopFact<DateTimeOffset>.Reported(departure.Value)
                : RailStopFact<DateTimeOffset>.Missing("terminal-not-provided"),
            RailStopFact<long>.Missing("missing-dwell"),
            RailTextFact.Missing("missing-platform"),
            []
        );
}
