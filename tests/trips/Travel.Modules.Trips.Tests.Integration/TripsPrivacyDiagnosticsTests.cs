using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsPrivacyDiagnosticsTests
{
    [Fact]
    public async Task Actual_http_logs_spans_and_errors_never_include_private_markers()
    {
        var ct = TestContext.Current.CancellationToken;
        var spans = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
                spans.Enqueue(
                    activity.DisplayName
                        + ";"
                        + string.Join(";", activity.TagObjects.Select(x => x.Key + "=" + x.Value))
                        + ";"
                        + string.Join(
                            ";",
                            activity
                                .Events.SelectMany(x => x.Tags)
                                .Select(x => x.Key + "=" + x.Value)
                        )
                ),
        };
        ActivitySource.AddActivityListener(listener);
        await using var fixture = await TripsHttpFixture.Start();
        var command = JsonNode.Parse(TripsHttpTests.CreateJson)!.AsObject();
        command["title"] = "private-title-marker-Ю";
        command["brief"]!["totalBudget"] = "123456789.99";
        using var created = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(command.ToJsonString()),
            ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();
        command["days"] = JsonNode.Parse(
            """[{"date":"2026-11-14","blocks":[{"blockId":"22222222-2222-4222-8222-222222222222","title":"private-block-marker","note":"private-note-marker <script>"}]}]"""
        );
        using var saved = await TripsHttpTests.Put(
            fixture.Client,
            location,
            command.ToJsonString(),
            "\"1\""
        );
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        command["forged"] = "private-forged-marker";
        using var invalid = await TripsHttpTests.Put(
            fixture.Client,
            location,
            command.ToJsonString(),
            "\"2\""
        );
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadAsStringAsync(ct);
        using var invalidCursor = await fixture.Client.GetAsync(
            "/api/trips?cursor=private-query-marker",
            ct
        );
        invalidCursor.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalidCursor.Content.ReadAsStringAsync(ct)).ShouldNotContain(
            "private-query-marker"
        );
        var markers = new[]
        {
            "private-title-marker",
            "private-block-marker",
            "private-note-marker",
            "private-forged-marker",
            "123456789.99",
            "private-query-marker",
        };
        fixture.Logs.Values.ShouldNotBeEmpty();
        fixture.Logs.Overflow.ShouldBeFalse();
        spans.ShouldNotBeEmpty();
        foreach (var marker in markers)
        {
            problem.ShouldNotContain(marker);
            fixture.Logs.Values.ShouldAllBe(value =>
                !value.Contains(marker, StringComparison.Ordinal)
            );
            spans.ShouldAllBe(value => !value.Contains(marker, StringComparison.Ordinal));
        }
        invalid.Headers.CacheControl!.NoStore.ShouldBeTrue();
        saved.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }
}
