using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsHttpTests
{
    internal const string CreateJson =
        """{"title":"Выходной в Петербурге","brief":{"destinationId":"saint-petersburg","originId":"moscow","startDate":"2026-11-14","endDate":"2026-11-14","adults":2,"currency":"RUB","totalBudget":"15000.00","interests":["architecture","food"],"pace":"relaxed"}}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Malformed_date_has_fixed_private_problem()
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var response = await fixture.Client.PostAsync(
            "/api/trips",
            Body(CreateJson.Replace("2026-11-14", "private-invalid-date")),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("Trips.InvalidInput");
        text.ShouldNotContain("private-invalid-date");
    }

    [Theory]
    [InlineData(false, 404)]
    [InlineData(true, 401)]
    public async Task Anonymous_discovery_distinguishes_disabled_without_resolving_dependencies(
        bool enabled,
        int expected
    )
    {
        await using var fixture = await TripsHttpFixture.Start(enabled);
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var response = await fixture.Client.GetAsync("/api/trips/destinations", Ct);
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Store.Rows.ShouldBeEmpty();
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Enabled_but_unready_returns_fixed_unavailable()
    {
        await using var fixture = await TripsHttpFixture.Start(ready: false);
        using var response = await fixture.Client.GetAsync("/api/trips/destinations", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Trips.Unavailable");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Authenticated_invalid_subject_is_forbidden_before_private_work(string subject)
    {
        await using var fixture = await TripsHttpFixture.Start();
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", subject);
        using var response = await fixture.Client.PostAsync(
            "/api/trips",
            Body("private-invalid-json"),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        fixture.Protection.Reads.ShouldBe(0);
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Conflicting_subjects_are_forbidden()
    {
        await using var fixture = await TripsHttpFixture.Start();
        fixture.Client.DefaultRequestHeaders.Add("X-Trips-OtherSubject", Guid.NewGuid().ToString());
        using var response = await fixture.Client.GetAsync("/api/trips", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("revision")]
    [InlineData("tripId")]
    [InlineData("createdAt")]
    [InlineData("warnings")]
    public async Task Forged_server_fields_rejected_without_creating(string field)
    {
        await using var fixture = await TripsHttpFixture.Start();
        var body = JsonNode.Parse(CreateJson)!.AsObject();
        body[field] = "private-forged";
        using var response = await fixture.Client.PostAsync(
            "/api/trips",
            Body(body.ToJsonString()),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("private-forged");
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"1\"")]
    [InlineData("\"1\",\"2\"")]
    [InlineData("\"01\"")]
    [InlineData("\"0\"")]
    [InlineData("\"9223372036854775808\"")]
    public async Task Invalid_etags_rejected_before_payload(string tag)
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/trips/11111111-1111-4111-8111-111111111111"
        );
        request.Headers.TryAddWithoutValidation("If-Match", tag);
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Missing_precondition_is_428()
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var response = await fixture.Client.DeleteAsync(
            "/api/trips/11111111-1111-4111-8111-111111111111",
            Ct
        );
        ((int)response.StatusCode).ShouldBe(428);
    }

    [Fact]
    public async Task Input_byte_bound_and_duplicate_members_rejected()
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var oversized = await fixture.Client.PostAsync(
            "/api/trips",
            Body(new string('x', 128 * 1024 + 1)),
            Ct
        );
        oversized.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await oversized.Content.ReadAsStringAsync(Ct)).ShouldContain("Trips.PlanTooLarge");
        using var duplicate = await fixture.Client.PostAsync(
            "/api/trips",
            Body(CreateJson.Replace("{\"title\"", "{\"title\":\"duplicate\",\"title\"")),
            Ct
        );
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Common_vector_create_replace_reload_noop_stale_owner_denial_delete()
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var created = await fixture.Client.PostAsync("/api/trips", Body(CreateJson), Ct);
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync(Ct)
        );
        created.Headers.ETag!.Tag.ShouldBe("\"1\"");
        created.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var location = created.Headers.Location!.ToString();
        using var detail = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        detail.RootElement.GetProperty("revision").ValueKind.ShouldBe(JsonValueKind.String);
        detail
            .RootElement.GetProperty("days")[0]
            .GetProperty("blocks")
            .GetArrayLength()
            .ShouldBe(0);
        detail.RootElement.GetProperty("knownUserEstimateSubtotal").GetString().ShouldBe("0.00");
        var replacement = JsonNode.Parse(CreateJson)!.AsObject();
        replacement["days"] = JsonNode.Parse(
            """[{"date":"2026-11-14","blocks":[{"blockId":"22222222-2222-4222-8222-222222222222","title":"Прогулка","note":null,"startTime":"10:00","endTime":"11:30","estimate":{"amount":"500.00","currency":"RUB"}}]}]"""
        );
        using var saved = await Put(fixture.Client, location, replacement.ToJsonString(), "\"1\"");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var savedJson = await saved.Content.ReadAsStringAsync(Ct);
        using var reload = await fixture.Client.GetAsync(location, Ct);
        (await reload.Content.ReadAsStringAsync(Ct)).ShouldBe(savedJson);
        using var noop = await Put(fixture.Client, location, replacement.ToJsonString(), "\"2\"");
        noop.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await noop.Content.ReadAsStringAsync(Ct)).ShouldBe(savedJson);
        using var stale = await Put(fixture.Client, location, replacement.ToJsonString(), "\"1\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var reads = fixture.Protection.Reads;
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        using var denied = await fixture.Client.GetAsync(location, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        fixture.Protection.Reads.ShouldBe(reads);
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        fixture.Client.DefaultRequestHeaders.Add(
            "X-Trips-Owner",
            TripsHttpFixture.Owner.ToString()
        );
        using var delete = new HttpRequestMessage(HttpMethod.Delete, location);
        delete.Headers.IfMatch.Add(new EntityTagHeaderValue("\"2\""));
        using var deleted = await fixture.Client.SendAsync(delete, Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var missing = await fixture.Client.GetAsync(location, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var noUpsert = await Put(
            fixture.Client,
            location,
            replacement.ToJsonString(),
            "\"2\""
        );
        noUpsert.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task City_change_conflict_preserves_saved_plan()
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var created = await fixture.Client.PostAsync("/api/trips", Body(CreateJson), Ct);
        var location = created.Headers.Location!.ToString();
        var replacement = JsonNode.Parse(CreateJson)!.AsObject();
        replacement["days"] = JsonNode.Parse(
            """[{"date":"2026-11-14","blocks":[{"blockId":"22222222-2222-4222-8222-222222222222","title":"Прогулка"}]}]"""
        );
        using var saved = await Put(fixture.Client, location, replacement.ToJsonString(), "\"1\"");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        replacement["brief"]!["destinationId"] = "moscow";
        using var conflict = await Put(
            fixture.Client,
            location,
            replacement.ToJsonString(),
            "\"2\""
        );
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("\\ud800")]
    [InlineData("\\udfff")]
    public async Task Invalid_decoded_unicode_is_a_fixed_problem(string invalid)
    {
        await using var fixture = await TripsHttpFixture.Start();
        using var response = await fixture.Client.PostAsync(
            "/api/trips",
            Body(CreateJson.Replace("Выходной в Петербурге", invalid.Replace("\\\\", "\\"))),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Trips.InvalidInput");
        fixture.Store.Rows.ShouldBeEmpty();
    }

    internal static async Task<HttpResponseMessage> Put(
        HttpClient client,
        string uri,
        string body,
        string tag
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = Body(body) };
        request.Headers.TryAddWithoutValidation("If-Match", tag);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal static StringContent Body(string value) =>
        new(value, Encoding.UTF8, "application/json");
}
