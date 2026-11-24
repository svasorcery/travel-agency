using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsManualStayHttpTests
{
    private const string Place = "Вымышленный гостевой дом";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stay_roundtrips_through_protected_detail_and_updates_only_with_explicit_intent()
    {
        await using var fixture = await TripsHttpFixture.Start();
        var client = fixture.Client;
        var command = JsonNode.Parse(TripsHttpTests.CreateJson)!.AsObject();
        command["brief"]!["endDate"] = "2026-11-15";
        var stay = new JsonObject
        {
            ["placeName"] = Place,
            ["checkInDate"] = "2026-11-14",
            ["checkOutDate"] = "2026-11-15",
        };
        command["manualStay"] = stay.DeepClone();
        using var invalidPost = await client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(command.ToJsonString()),
            Ct
        );
        invalidPost.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        command.Remove("manualStay").ShouldBeTrue();
        using var created = await client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(command.ToJsonString()),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var path = created.Headers.Location!.ToString();
        command["days"] = JsonNode.Parse(
            """[{"date":"2026-11-14","blocks":[]},{"date":"2026-11-15","blocks":[]}]"""
        );
        command["manualStay"] = stay.DeepClone();
        using var saved = await TripsHttpTests.Put(client, path, command.ToJsonString(), "\"1\"");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        using var detail = await client.GetAsync(path, Ct);
        var accepted = await detail.Content.ReadAsStringAsync(Ct);
        JsonNode.DeepEquals(JsonNode.Parse(accepted)!["manualStay"], stay).ShouldBeTrue();
        detail.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var list = await client.GetAsync("/api/trips", Ct);
        var summary = JsonNode.Parse(await list.Content.ReadAsStringAsync(Ct))!["items"]![
            0
        ]!.AsObject();
        summary.ContainsKey("manualStay").ShouldBeFalse();

        command.Remove("manualStay").ShouldBeTrue();
        using var noop = await TripsHttpTests.Put(client, path, command.ToJsonString(), "\"2\"");
        noop.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await noop.Content.ReadAsStringAsync(Ct)).ShouldBe(accepted);
        using var duplicate = await TripsHttpTests.Put(
            client,
            path,
            command.ToJsonString()[..^1] + ""","manualStay":null,"manualStay":null}""",
            "\"2\""
        );
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var invalidStay = stay.DeepClone().AsObject();
        invalidStay["confirmed"] = true;
        command["manualStay"] = invalidStay;
        using var extra = await TripsHttpTests.Put(client, path, command.ToJsonString(), "\"2\"");
        extra.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        command.Remove("manualStay").ShouldBeTrue();

        using var foreign = new HttpRequestMessage(HttpMethod.Get, path);
        foreign.Headers.Add("X-Trips-Owner", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        using var denied = await client.SendAsync(foreign, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await denied.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Place);

        command["brief"]!["endDate"] = "2026-11-14";
        command["days"] = JsonNode.Parse("""[{"date":"2026-11-14","blocks":[]}]""");
        using var conflict = await TripsHttpTests.Put(
            client,
            path,
            command.ToJsonString(),
            "\"2\""
        );
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await conflict.Content.ReadAsStringAsync(Ct);
        problem.ShouldContain("Trips.DateRangeWouldExcludeStay");
        problem.ShouldNotContain(Place);

        command["brief"]!["destinationId"] = "kaliningrad";
        command["manualStay"] = null;
        using var city = await TripsHttpTests.Put(client, path, command.ToJsonString(), "\"2\"");
        city.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await city.Content.ReadAsStringAsync(Ct)).ShouldContain(
            "Trips.DestinationChangeRequiresRemovingStay"
        );
        command["brief"]!["destinationId"] = "saint-petersburg";
        using var stale = await TripsHttpTests.Put(client, path, command.ToJsonString(), "\"1\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var afterConflicts = await client.GetAsync(path, Ct);
        (await afterConflicts.Content.ReadAsStringAsync(Ct)).ShouldBe(accepted);
        using var removal = await TripsHttpTests.Put(client, path, command.ToJsonString(), "\"2\"");
        removal.StatusCode.ShouldBe(HttpStatusCode.OK);
        removal.Headers.ETag!.Tag.ShouldBe("\"3\"");
        JsonNode
            .Parse(await removal.Content.ReadAsStringAsync(Ct))!
            .AsObject()
            .ContainsKey("manualStay")
            .ShouldBeFalse();
    }
}
