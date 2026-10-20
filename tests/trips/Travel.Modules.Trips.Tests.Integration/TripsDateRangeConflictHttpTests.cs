using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsDateRangeConflictHttpTests
{
    [Fact]
    public async Task Shortening_range_with_populated_second_day_returns_409_and_preserves_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await TripsHttpFixture.Start();
        var command = JsonNode.Parse(TripsHttpTests.CreateJson)!.AsObject();
        command["brief"]!["endDate"] = "2026-11-15";
        using var created = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(command.ToJsonString()),
            ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();
        command["days"] = JsonNode.Parse(
            """[{"date":"2026-11-14","blocks":[]},{"date":"2026-11-15","blocks":[{"blockId":"22222222-2222-4222-8222-222222222222","title":"Second-day walk","note":"Fictional private second-day note"}]}]"""
        );
        using var saved = await TripsHttpTests.Put(
            fixture.Client,
            location,
            command.ToJsonString(),
            "\"1\""
        );
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var accepted = await saved.Content.ReadAsStringAsync(ct);

        command["brief"]!["endDate"] = "2026-11-14";
        command["days"] = JsonNode.Parse("""[{"date":"2026-11-14","blocks":[]}]""");
        using var conflict = await TripsHttpTests.Put(
            fixture.Client,
            location,
            command.ToJsonString(),
            "\"2\""
        );
        var problem = await conflict.Content.ReadAsStringAsync(ct);
        problem.ShouldContain("Trips.DateRangeWouldDropBlocks");
        problem.ShouldNotContain("Fictional private second-day note");
        conflict.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var reloaded = await fixture.Client.GetAsync(location, ct);
        reloaded.StatusCode.ShouldBe(HttpStatusCode.OK);
        reloaded.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await reloaded.Content.ReadAsStringAsync(ct)).ShouldBe(accepted);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
