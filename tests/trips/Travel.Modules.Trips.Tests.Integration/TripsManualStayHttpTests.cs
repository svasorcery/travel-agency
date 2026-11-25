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
    public async Task Versioned_workspaces_share_the_revision_without_exposing_a_partial_legacy_detail()
    {
        await using var fixture = await TripsHttpFixture.Start();
        var (path, command) = await CreateWorkspace(fixture);
        var v2 = path + "/workspace-v2";
        using var initial = await fixture.Client.GetAsync(v2, Ct);
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        initial.Headers.ETag!.Tag.ShouldBe("\"1\"");
        initial.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var empty = JsonNode.Parse(await initial.Content.ReadAsStringAsync(Ct))!.AsObject();
        empty.ContainsKey("manualStays").ShouldBeFalse();
        empty.ContainsKey("manualStay").ShouldBeFalse();

        var first = Stay(Place, "14", "15");
        var second = Stay("Вымышленные апартаменты", "15", "17");
        command["manualStays"] = new JsonArray(second.DeepClone(), first.DeepClone());
        using var saved = await TripsHttpTests.Put(
            fixture.Client,
            v2,
            command.ToJsonString(),
            "\"1\""
        );
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var accepted = await saved.Content.ReadAsStringAsync(Ct);
        var detail = JsonNode.Parse(accepted)!.AsObject();
        detail.ContainsKey("manualStay").ShouldBeFalse();
        JsonNode
            .DeepEquals(detail["manualStays"], new JsonArray(first.DeepClone(), second.DeepClone()))
            .ShouldBeTrue();

        using var oldRead = await fixture.Client.GetAsync(path, Ct);
        await AssertVersionRequired(oldRead);
        command.Remove("manualStays").ShouldBeTrue();
        using var omitted = await TripsHttpTests.Put(
            fixture.Client,
            path,
            command.ToJsonString(),
            "\"2\""
        );
        await AssertVersionRequired(omitted);
        command["manualStay"] = null;
        using var clearLegacy = await TripsHttpTests.Put(
            fixture.Client,
            path,
            command.ToJsonString(),
            "\"2\""
        );
        await AssertVersionRequired(clearLegacy);
        command["manualStay"] = first.DeepClone();
        using var replaceLegacy = await TripsHttpTests.Put(
            fixture.Client,
            path,
            command.ToJsonString(),
            "\"2\""
        );
        await AssertVersionRequired(replaceLegacy);
        using var stale = await TripsHttpTests.Put(
            fixture.Client,
            path,
            command.ToJsonString(),
            "\"1\""
        );
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        command.Remove("manualStay").ShouldBeTrue();

        using var afterRefusal = await fixture.Client.GetAsync(v2, Ct);
        (await afterRefusal.Content.ReadAsStringAsync(Ct)).ShouldBe(accepted);
        await CreateWorkspace(fixture);
        using var mixedPage = await fixture.Client.GetAsync("/api/trips", Ct);
        mixedPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summaries = JsonNode.Parse(await mixedPage.Content.ReadAsStringAsync(Ct))![
            "items"
        ]!.AsArray();
        summaries.Count.ShouldBe(2);
        summaries.ShouldAllBe(summary =>
            !summary!.AsObject().ContainsKey("manualStay")
            && !summary.AsObject().ContainsKey("manualStays")
        );
        command["manualStays"] = new JsonArray(second.DeepClone());
        using var reduced = await TripsHttpTests.Put(
            fixture.Client,
            v2,
            command.ToJsonString(),
            "\"2\""
        );
        reduced.StatusCode.ShouldBe(HttpStatusCode.OK);
        reduced.Headers.ETag!.Tag.ShouldBe("\"3\"");
        using var legacyRead = await fixture.Client.GetAsync(path, Ct);
        var single = JsonNode.Parse(await legacyRead.Content.ReadAsStringAsync(Ct))!.AsObject();
        JsonNode.DeepEquals(single["manualStay"], second).ShouldBeTrue();
        single.ContainsKey("manualStays").ShouldBeFalse();

        command["manualStays"] = new JsonArray();
        using var cleared = await TripsHttpTests.Put(
            fixture.Client,
            v2,
            command.ToJsonString(),
            "\"3\""
        );
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var clearedDetail = JsonNode.Parse(await cleared.Content.ReadAsStringAsync(Ct))!.AsObject();
        clearedDetail.ContainsKey("manualStay").ShouldBeFalse();
        clearedDetail.ContainsKey("manualStays").ShouldBeFalse();
    }

    [Fact]
    public async Task Versioned_replacement_rejects_ambiguous_input_and_preserves_same_revision()
    {
        await using var fixture = await TripsHttpFixture.Start();
        var (path, command) = await CreateWorkspace(fixture);
        var workspace = path + "/workspace-v2";
        var original = fixture.Store.Rows.Values.Single();
        foreach (
            var invalid in new[]
            {
                command.ToJsonString(),
                command.ToJsonString()[..^1] + ""","manualStays":null}""",
                command.ToJsonString()[..^1] + ""","manualStays":[null]}""",
                command.ToJsonString()[..^1] + ""","manualStays":[],"manualStay":null}""",
                command.ToJsonString()[..^1] + ""","manualStays":[],"manualStays":[]}""",
            }
        )
        {
            using var rejected = await TripsHttpTests.Put(
                fixture.Client,
                workspace,
                invalid,
                "\"1\""
            );
            rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        command["manualStays"] = new JsonArray(Stay(Place, "14", "16"), Stay("Second", "15", "17"));
        using var overlap = await TripsHttpTests.Put(
            fixture.Client,
            workspace,
            command.ToJsonString(),
            "\"1\""
        );
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overlap.Content.ReadAsStringAsync(Ct)).ShouldContain("Trips.ManualStaysOverlap");
        command["manualStays"] = new JsonArray();
        using var missingTag = await fixture.Client.PutAsync(
            workspace,
            TripsHttpTests.Body(command.ToJsonString()),
            Ct
        );
        ((int)missingTag.StatusCode).ShouldBe(428);
        using var foreign = new HttpRequestMessage(HttpMethod.Put, workspace)
        {
            Content = TripsHttpTests.Body(command.ToJsonString()),
        };
        foreign.Headers.Add("X-Trips-Owner", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        foreign.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var denied = await fixture.Client.SendAsync(foreign, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        fixture.Store.Rows.Values.Single().ShouldBe(original);
    }

    [Fact]
    public async Task Concurrent_legacy_and_v2_updates_cannot_both_commit_the_same_revision()
    {
        await using var fixture = await TripsHttpFixture.Start();
        var (path, command) = await CreateWorkspace(fixture);
        var workspace = path + "/workspace-v2";
        var first = Stay(Place, "14", "15");
        command["manualStay"] = first.DeepClone();
        using var withOneStay = await TripsHttpTests.Put(
            fixture.Client,
            path,
            command.ToJsonString(),
            "\"1\""
        );
        withOneStay.StatusCode.ShouldBe(HttpStatusCode.OK);
        command.Remove("manualStay").ShouldBeTrue();
        var upgraded = command.DeepClone().AsObject();
        upgraded["manualStays"] = new JsonArray(first.DeepClone(), Stay("Second", "15", "17"));
        command["title"] = "Fictional legacy edit";
        var results = await Task.WhenAll(
            TripsHttpTests.Put(fixture.Client, workspace, upgraded.ToJsonString(), "\"2\""),
            TripsHttpTests.Put(fixture.Client, path, command.ToJsonString(), "\"2\"")
        );
        using var v2 = results[0];
        using var legacy = results[1];
        results
            .Select(result => result.StatusCode)
            .Order()
            .ShouldBe(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed });
        using var accepted = await fixture.Client.GetAsync(workspace, Ct);
        accepted.Headers.ETag!.Tag.ShouldBe("\"3\"");
        var detail = JsonNode.Parse(await accepted.Content.ReadAsStringAsync(Ct))!;
        if (v2.StatusCode == HttpStatusCode.OK)
        {
            detail["manualStays"]!.AsArray().Count.ShouldBe(2);
            using var refused = await TripsHttpTests.Put(
                fixture.Client,
                path,
                command.ToJsonString(),
                "\"3\""
            );
            await AssertVersionRequired(refused);
        }
        else
        {
            JsonNode.DeepEquals(detail["manualStay"], first).ShouldBeTrue();
            detail["title"]!.GetValue<string>().ShouldBe("Fictional legacy edit");
        }
    }

    private static async Task AssertVersionRequired(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var problem = await response.Content.ReadAsStringAsync(Ct);
        problem.ShouldContain("Trips.PlanVersionRequired");
        problem.ShouldNotContain(Place);
    }

    private static async Task<(string Path, JsonObject Command)> CreateWorkspace(
        TripsHttpFixture fixture
    )
    {
        var command = JsonNode.Parse(TripsHttpTests.CreateJson)!.AsObject();
        command["brief"]!["endDate"] = "2026-11-17";
        using var created = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(command.ToJsonString()),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        command["days"] = new JsonArray(
            Enumerable
                .Range(14, 4)
                .Select(day => new JsonObject
                {
                    ["date"] = $"2026-11-{day}",
                    ["blocks"] = new JsonArray(),
                })
                .ToArray()
        );
        return (created.Headers.Location!.ToString(), command);
    }

    private static JsonObject Stay(string place, string checkIn, string checkOut) =>
        new()
        {
            ["placeName"] = place,
            ["checkInDate"] = $"2026-11-{checkIn}",
            ["checkOutDate"] = $"2026-11-{checkOut}",
        };

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
