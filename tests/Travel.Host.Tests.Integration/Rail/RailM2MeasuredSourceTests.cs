using System.Text.Json;
using Shouldly;
using Travel.Modules.Rail.Core;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Host.Tests.Integration.Rail;

public sealed class RailM2MeasuredSourceTests
{
    [Theory]
    [InlineData("failed")]
    [InlineData("limited")]
    [InlineData("skipped")]
    public void Http200_nonuseful_outcomes_cannot_pass_measured_predicate(string kind)
    {
        using var data = JsonDocument.Parse("{\"kind\":\"" + kind + "\",\"stops\":[]}");
        RailM2MeasuredWorkload
            .UsefulDetail(
                200,
                data.RootElement,
                RailM2MeasuredWorkload.Input(
                    Query,
                    new string('a', 32),
                    DateTimeOffset.Parse("2026-10-21T00:00:00Z")
                )
            )
            .ShouldBeFalse();
        RailM2MeasuredWorkload
            .UsefulSearch(200, data.RootElement, Query, new string('a', 32))
            .ShouldBeFalse();
    }

    [Fact]
    public void Two_hundred_stop_pressure_fixture_passes_actual_parser_and_binding_predicate()
    {
        var input = RailM2MeasuredWorkload.Input(
            Query,
            new string('a', 32),
            DateTimeOffset.Parse("2026-10-21T00:00:00Z")
        );
        var selection = input.Selection;
        var journey = new ScheduledJourney(
            selection.Key,
            "measured-train",
            "train",
            Query.From,
            Query.To,
            selection.Departure,
            selection.Arrival,
            7200,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new("unknown", null, "not-provided", [], []),
            DateTimeOffset.MinValue,
            []
        );
        var route = YandexRailRouteParser.Parse(
            JsonSerializer.SerializeToUtf8Bytes(
                RailM2MeasuredWorkload.Thread("s1", "s2", Query.DepartureDate, "measured-train")
            ),
            journey,
            DateTimeOffset.MinValue,
            TestContext.Current.CancellationToken
        );
        route.Kind.ShouldBe("complete");
        route.Stops.Length.ShouldBe(200);
        using var body = JsonDocument.Parse(
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    route.Kind,
                    route.Reasons,
                    route.Stops,
                    route.BoardingIndex,
                    route.AlightingIndex,
                    request = input,
                    source = "yandex-rasp",
                    profile = "fictional",
                    profileEpoch = input.ExpectedProfileEpoch,
                    stage = "route",
                    verification = new
                    {
                        transport = "train",
                        kind = "complete",
                        reasons = Array.Empty<string>(),
                        receivedAt = "2026-10-21T00:00:00Z",
                    },
                    routeReceivedAt = "2026-10-21T00:00:00Z",
                    completedAt = "2026-10-21T00:00:00Z",
                    expiresAt = "2026-10-21T00:01:00Z",
                    attribution = new { frameUrl = "/rail-fictional-banner.html" },
                },
                JsonSerializerOptions.Web
            )
        );
        RailM2MeasuredWorkload.UsefulDetail(200, body.RootElement, input).ShouldBeTrue();
        RailM2MeasuredWorkload.UsefulDetail(500, body.RootElement, input).ShouldBeFalse();
        foreach (var defect in new[] { "request", "generation", "epoch", "verification", "expiry" })
        {
            var broken = System.Text.Json.Nodes.JsonNode.Parse(body.RootElement.GetRawText())!;
            if (defect == "request")
                broken["request"]!["selection"]!["key"] = "other";
            if (defect == "generation")
                broken["request"]!["search"]!["catalogueGeneration"] = new string('b', 32);
            if (defect == "epoch")
                broken["profileEpoch"] = new string('b', 32);
            if (defect == "verification")
                broken["verification"] = null;
            if (defect == "expiry")
                broken["expiresAt"] = "2026-10-21T01:00:00Z";
            using var invalid = JsonDocument.Parse(broken.ToJsonString());
            RailM2MeasuredWorkload
                .UsefulDetail(200, invalid.RootElement, input)
                .ShouldBeFalse(defect);
        }
        RailM2MeasuredWorkload
            .UsefulDetail(
                200,
                body.RootElement,
                input with
                {
                    Search = Query with { To = StationRef.Create("yandex-rasp", "s9").Value },
                }
            )
            .ShouldBeFalse();
    }

    private static RailRequest Query =>
        new(
            StationRef.Create("yandex-rasp", "s1").Value,
            StationRef.Create("yandex-rasp", "s2").Value,
            new string('a', 32),
            "2026-10-21",
            "all"
        );
}
