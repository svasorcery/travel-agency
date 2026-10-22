using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Rail.Core;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class RouteParserTests
{
    [Fact]
    public void Interval_only_shape_is_explicitly_unsupported_and_empty_platform_stays_independently_missing()
    {
        var wire = Wire();
        wire["interval"] = new JsonObject();
        var interval = Parse(wire);
        interval.Reasons.ShouldContain("unsupported-route-shape");
        interval.Stops.ShouldBeEmpty();
        wire = Wire();
        wire["stops"]![1]!["platform"] = "";
        var route = Parse(wire);
        route.Kind.ShouldBe("complete");
        route.Stops[1].Platform.State.ShouldBe("not-provided");
        route.Stops[1].Arrival.State.ShouldBe("reported");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Token_or_depth_overflow_is_output_limit_not_mapping(bool depth)
    {
        var raw = Wire().ToJsonString();
        raw =
            raw[..^1]
            + ",\"extra\":"
            + (
                depth
                    ? new string('[', 33) + "0" + new string(']', 33)
                    : "\"" + new string('x', 16385) + "\""
            )
            + "}";
        var result = YandexRailRouteParser.Parse(
            Encoding.UTF8.GetBytes(raw),
            Journey,
            Received,
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe("failed");
        result.Reasons.ShouldContain("output-limit");
        result.Stops.ShouldBeEmpty();
    }

    internal static readonly DateTimeOffset Received = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
    internal static ScheduledJourney Journey =>
        new(
            "selected-key",
            "fictional+uid",
            "train",
            StationRef.Create("yandex-rasp", "s1").Value,
            StationRef.Create("yandex-rasp", "s2").Value,
            DateTimeOffset.Parse("2026-11-02T00:00:00+03:00"),
            DateTimeOffset.Parse("2026-11-02T02:00:00+04:00"),
            3600,
            "2026-10-30",
            null,
            null,
            null,
            null,
            null,
            null,
            new("unknown", null, "not-provided", [], []),
            Received,
            []
        );

    internal static JsonObject Wire() =>
        JsonNode
            .Parse(
                """
                {"uid":"fictional+uid","transport_type":"train","departure_date":"2026-11-02","arrival_date":"2026-11-02","stops":[
                {"station":{"code":"s1","title":"Учебная Северная","type":"station"},"arrival":null,"departure":"2026-11-02T00:00:00+03:00","stop_time":null},
                {"station":{"code":"s3","title":"Учебная Средняя","type":"station"},"arrival":"2026-11-02T00:30:00+03:00","departure":"2026-11-02T00:31:00+03:00","stop_time":60.0,"platform":"2"},
                {"station":{"code":"s2","title":"Учебная Южная","type":"station"},"arrival":"2026-11-02T02:00:00+04:00","departure":null,"stop_time":0}]}
                """
            )!
            .AsObject();

    private static Travel.Modules.Rail.Application.RailRouteResult Parse(JsonNode wire) =>
        YandexRailRouteParser.Parse(
            Encoding.UTF8.GetBytes(wire.ToJsonString()),
            Journey,
            Received,
            TestContext.Current.CancellationToken
        );

    [Fact]
    public void Date_only_echo_and_terminal_nulls_preserve_complete_route_and_exact_zero()
    {
        var result = Parse(Wire());
        result.Kind.ShouldBe("complete");
        result.Stops.Length.ShouldBe(3);
        result.BoardingIndex.ShouldBe(0);
        result.AlightingIndex.ShouldBe(2);
        result.Stops[1].DwellSeconds.Value.ShouldBe(60);
        result.Stops[2].DwellSeconds.Value.ShouldBe(0);
        result.Stops[0].Arrival.Reason.ShouldBe("terminal-not-provided");
    }

    [Theory]
    [InlineData("2026-11-02T23:00:00+14:00", "complete")]
    [InlineData("2026-11-03", "failed")]
    [InlineData("unknown", "limited")]
    public void Echo_is_calendar_evidence_only(string echo, string expected)
    {
        var wire = Wire();
        wire["departure_date"] = echo;
        Parse(wire).Kind.ShouldBe(expected);
    }

    [Theory]
    [InlineData("arrival", "2026-11-02T00:30:00", "limited")]
    [InlineData("departure", "2026-11-02T00:31:00.1+03:00", "limited")]
    [InlineData("arrival", "2026-11-01T00:30:00+03:00", "failed")]
    public void Intermediate_time_is_independent_but_known_chronology_is_required(
        string field,
        string value,
        string expected
    )
    {
        var wire = Wire();
        wire["stops"]![1]![field] = value;
        Parse(wire).Kind.ShouldBe(expected);
    }

    [Theory]
    [InlineData("60.0", "reported", 60L)]
    [InlineData("60.00000000000000000001", "unavailable", null)]
    [InlineData("-1", "unavailable", null)]
    [InlineData("9007199254740992", "unavailable", null)]
    [InlineData("0", "unavailable", null)]
    public void Dwell_is_exact_before_numeric_conversion_and_consistent_with_own_times(
        string number,
        string state,
        long? value
    )
    {
        var body = Wire()
            .ToJsonString(
                new System.Text.Json.JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            )
            .Replace("60.0", number, StringComparison.Ordinal);
        var result = YandexRailRouteParser.Parse(
            Encoding.UTF8.GetBytes(body),
            Journey,
            Received,
            TestContext.Current.CancellationToken
        );
        result.Stops[1].DwellSeconds.State.ShouldBe(state);
        result.Stops[1].DwellSeconds.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(
        "\"uid\":\"fictional+uid\",",
        "\"uid\":\"fictional+uid\",\"uid\":\"fictional+uid\",",
        "failed"
    )]
    [InlineData("\"stop_time\":60.0", "\"stop_time\":60.0,\"stop_time\":60", "limited")]
    [InlineData("\"departure\":\"2026-11-02T00:00:00+03:00\"", "\"departure\":null", "failed")]
    [InlineData("\"type\":\"station\"", "\"type\":\"settlement\"", "failed")]
    public void Structural_damage_fails_and_optional_duplicates_remain_local(
        string old,
        string replacement,
        string expected
    ) =>
        YandexRailRouteParser
            .Parse(
                Encoding.UTF8.GetBytes(
                    Wire()
                        .ToJsonString(
                            new System.Text.Json.JsonSerializerOptions
                            {
                                Encoder = System
                                    .Text
                                    .Encodings
                                    .Web
                                    .JavaScriptEncoder
                                    .UnsafeRelaxedJsonEscaping,
                            }
                        )
                        .Replace(old, replacement, StringComparison.Ordinal)
                ),
                Journey,
                Received,
                TestContext.Current.CancellationToken
            )
            .Kind.ShouldBe(expected);

    [Theory]
    [InlineData(200, "complete")]
    [InlineData(201, "failed")]
    public void Stop_bound_is_checked_without_truncation(int count, string kind)
    {
        var wire = Wire();
        var rows = wire["stops"]!.AsArray();
        rows[1]!["departure"] = "2026-11-02T00:30:00+03:00";
        rows[1]!["stop_time"] = 0;
        while (rows.Count < count)
            rows.Insert(1, rows[1]!.DeepClone());
        // Equal instants are permitted. Repeated intermediate refs do not change the selected pair.
        Parse(wire).Kind.ShouldBe(kind);
    }

    [Theory]
    [InlineData("\"s1\"", "complete")]
    [InlineData("null", "complete")]
    [InlineData("\"s9\"", "failed")]
    [InlineData("{}", "failed")]
    public void Thread_endpoint_echo_is_an_optional_string_code(string echo, string kind)
    {
        var wire = Wire();
        wire["from"] = JsonNode.Parse(echo);
        Parse(wire).Kind.ShouldBe(kind);
    }
}
