using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripsHostTests
{
    [Fact]
    public async Task Actual_Host_OpenAPI_distinguishes_the_legacy_detail_from_workspace_v2()
    {
        await using var fixture = new TripsHostFixture();
        await fixture.StartAsync(Ct);
        using var response = await fixture.Client.GetAsync("/openapi/v1.json", Ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        var legacy = paths.GetProperty("/api/trips/{tripId}");
        var workspace = paths.GetProperty("/api/trips/{tripId}/workspace-v2");

        foreach (var method in new[] { "get", "put" })
        {
            var legacyResponses = legacy.GetProperty(method).GetProperty("responses");
            var workspaceResponses = workspace.GetProperty(method).GetProperty("responses");
            legacyResponses.TryGetProperty("409", out _).ShouldBeTrue();
            var oldDetail = Schema(legacyResponses.GetProperty("200"));
            oldDetail.GetProperty("properties").TryGetProperty("manualStay", out _).ShouldBeTrue();
            oldDetail
                .GetProperty("properties")
                .TryGetProperty("manualStays", out _)
                .ShouldBeFalse();

            var success = workspaceResponses.GetProperty("200");
            success.GetProperty("headers").TryGetProperty("ETag", out _).ShouldBeTrue();
            var variants = Schema(success).GetProperty("oneOf").EnumerateArray().ToArray();
            variants.Length.ShouldBe(2);
            variants[0]
                .GetProperty("properties")
                .TryGetProperty("manualStay", out _)
                .ShouldBeTrue();
            var multi = variants[1].GetProperty("properties");
            multi.TryGetProperty("manualStay", out _).ShouldBeFalse();
            multi.GetProperty("manualStays").GetProperty("minItems").GetInt32().ShouldBe(2);
            multi.GetProperty("manualStays").GetProperty("maxItems").GetInt32().ShouldBe(13);
        }

        var put = workspace.GetProperty("put");
        var body = Schema(put.GetProperty("requestBody"));
        body.GetProperty("required")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .ShouldContain("manualStays");
        body.GetProperty("properties").TryGetProperty("manualStay", out _).ShouldBeFalse();
        put.GetProperty("parameters")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "If-Match")
            .GetProperty("required")
            .GetBoolean()
            .ShouldBeTrue();
        put.GetProperty("responses").TryGetProperty("412", out _).ShouldBeTrue();
        put.GetProperty("responses").TryGetProperty("428", out _).ShouldBeTrue();

        JsonElement Schema(JsonElement message)
        {
            var schema = message
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema");
            return schema.TryGetProperty("$ref", out var reference)
                ? document
                    .RootElement.GetProperty("components")
                    .GetProperty("schemas")
                    .GetProperty(reference.GetString()!.Split('/')[^1])
                : schema;
        }
    }

    [Fact]
    public async Task Actual_Host_OpenAPI_declares_version_header_and_city_query_on_existing_routes()
    {
        await using var fixture = new TripsHostFixture(new SchemaOnlyFacts());
        await fixture.StartAsync(Ct);
        using var response = await fixture.Client.GetAsync("/openapi/v1.json", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        var available = paths.GetProperty("/api/trips/itinerary-availability").GetProperty("get");
        var candidate = paths
            .GetProperty("/api/trips/{tripId}/itinerary-candidate")
            .GetProperty("post");
        foreach (var operation in new[] { available, candidate })
        {
            var version = operation
                .GetProperty("parameters")
                .EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == "X-Trips-Itinerary-Version");
            version.GetProperty("in").GetString().ShouldBe("header");
            (
                version.TryGetProperty("required", out var required) && required.GetBoolean()
            ).ShouldBeFalse();
            operation.GetProperty("responses").TryGetProperty("200", out _).ShouldBeTrue();
        }
        var city = available
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "destinationId");
        city.GetProperty("in").GetString().ShouldBe("query");
        (
            city.TryGetProperty("required", out var cityRequired) && cityRequired.GetBoolean()
        ).ShouldBeFalse();
        available.TryGetProperty("requestBody", out _).ShouldBeFalse();
        TestContext.Current.TestOutputHelper?.WriteLine(
            "TRIPS_M22_OPENAPI:"
                + JsonSerializer.Serialize(new { availability = available, candidate })
        );
    }

    [Fact]
    public async Task Preview_OpenAPI_exposes_the_unsaved_brief_and_all_outcomes()
    {
        await using var fixture = new TripsHostFixture(new SchemaOnlyFacts());
        await fixture.StartAsync(Ct);
        using var response = await fixture.Client.GetAsync("/openapi/v1.json", Ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        var metadata = paths
            .GetProperty("/api/trips/option-preview/availability")
            .GetProperty("get");
        metadata.TryGetProperty("requestBody", out _).ShouldBeFalse();
        var post = paths.GetProperty("/api/trips/option-preview").GetProperty("post");
        var saveHeader = post.GetProperty("parameters")
            .EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "X-Trips-Option-Save");
        saveHeader.GetProperty("in").GetString().ShouldBe("header");
        saveHeader.GetProperty("schema").GetProperty("pattern").GetString().ShouldBe("^1$");
        (
            saveHeader.TryGetProperty("required", out var required) && required.GetBoolean()
        ).ShouldBeFalse();
        var request = Resolve(post.GetProperty("requestBody"));
        request.GetProperty("properties").EnumerateObject().Select(p => p.Name).ShouldBe(["brief"]);
        foreach (
            var status in new[] { "200", "400", "401", "403", "404", "413", "429", "503", "504" }
        )
            post.GetProperty("responses").TryGetProperty(status, out _).ShouldBeTrue();
        var formats = Resolve(post.GetProperty("responses").GetProperty("200"))
            .GetProperty("oneOf")
            .EnumerateArray()
            .Select(Deref)
            .ToArray();
        formats.Length.ShouldBe(2);
        formats[0].GetProperty("properties").TryGetProperty("option", out _).ShouldBeTrue();
        formats[1].GetProperty("properties").TryGetProperty("selection", out _).ShouldBeTrue();
        var save = paths.GetProperty("/api/trips/saved-options").GetProperty("post");
        Resolve(save.GetProperty("requestBody"))
            .GetProperty("properties")
            .TryGetProperty("selectionToken", out _)
            .ShouldBeTrue();
        save.GetProperty("responses")
            .GetProperty("201")
            .GetProperty("headers")
            .TryGetProperty("Location", out _)
            .ShouldBeTrue();
        var history = paths.GetProperty("/api/trips/{tripId}/saved-option").GetProperty("get");
        Resolve(history.GetProperty("responses").GetProperty("200"))
            .GetProperty("properties")
            .TryGetProperty("savedOption", out _)
            .ShouldBeTrue();

        JsonElement Resolve(JsonElement message) =>
            Deref(
                message.GetProperty("content").GetProperty("application/json").GetProperty("schema")
            );
        JsonElement Deref(JsonElement schema)
        {
            return schema.TryGetProperty("$ref", out var reference)
                ? document
                    .RootElement.GetProperty("components")
                    .GetProperty("schemas")
                    .GetProperty(reference.GetString()!.Split('/')[^1])
                : schema;
        }
    }

    private sealed class SchemaOnlyFacts : IItineraryFactSource
    {
        public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            DestinationSelection destination,
            CancellationToken ct
        ) => throw new InvalidOperationException("OpenAPI must not read city facts.");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static object CreateBody =>
        new
        {
            title = "Fictional Moscow private plan",
            brief = new
            {
                destinationId = "moscow",
                originId = (string?)null,
                startDate = "2026-10-25",
                endDate = "2026-10-25",
                adults = 2,
                currency = "RUB",
                totalBudget = (string?)null,
                interests = new[] { "history", "nature" },
                pace = "balanced",
            },
        };

    [Fact]
    public async Task Actual_Host_creates_protected_plan_and_preserves_edits_CAS_owner_and_restart()
    {
        await using var fixture = new TripsHostFixture();
        await fixture.StartAsync(Ct);
        using var created = await fixture.Client.PostAsJsonAsync("/api/trips", CreateBody, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var plan = JsonNode.Parse(await created.Content.ReadAsStringAsync(Ct))!;
        var id = plan["tripId"]!.GetValue<string>();
        created.Headers.ETag!.Tag.ShouldBe("\"1\"");
        created.Headers.Location!.OriginalString.ShouldBe("/api/trips/" + id);
        var blockId = Guid.NewGuid();
        var change = new
        {
            title = "Fictional edited private plan",
            brief = plan["brief"]!.DeepClone(),
            days = new[]
            {
                new
                {
                    date = "2026-10-25",
                    blocks = new[]
                    {
                        new
                        {
                            blockId,
                            title = "Fictional garden",
                            note = "Private fictional note",
                            startTime = "10:00",
                            endTime = "10:45",
                            estimate = new { amount = "450.00", currency = "RUB" },
                        },
                    },
                },
            },
        };
        using var put = Write(id, "1", change);
        using var saved = await fixture.Client.SendAsync(put, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var expected = await saved.Content.ReadAsStringAsync(Ct);
        var savedPlan = JsonNode.Parse(expected)!;
        savedPlan["days"]![0]!["blocks"]![0]!["blockId"]!.GetValue<Guid>().ShouldBe(blockId);
        savedPlan["knownUserEstimateSubtotal"]!.GetValue<string>().ShouldBe("450.00");
        string envelope;
        await using (var db = fixture.Db())
        {
            var row = await db.Plans.AsNoTracking().SingleAsync(Ct);
            row.Revision.ShouldBe(2);
            row.OwnerIssuer.ShouldBe(TripsHostFixture.Issuer);
            row.OwnerSubject.ShouldBe(TripsHostFixture.Owner);
            row.ProtectedPayload.ShouldNotContain("Private fictional note");
            row.ProtectedPayload.ShouldNotContain("Fictional edited private plan");
            envelope = row.ProtectedPayload;
        }
        using var stale = Write(id, "1", change);
        using var staleResult = await fixture.Client.SendAsync(stale, Ct);
        staleResult.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/trips/" + id);
        foreign.Headers.Add("X-Trips-Owner", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        using var foreignResult = await fixture.Client.SendAsync(foreign, Ct);
        foreignResult.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await fixture.RestartAsync();
        using var reopened = await fixture.Client.GetAsync("/api/trips/" + id, Ct);
        reopened.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reopened.Content.ReadAsStringAsync(Ct)).ShouldBe(expected);
        reopened.Headers.ETag!.Tag.ShouldBe("\"2\"");
        await using var verify = fixture.Db();
        (await verify.Plans.AsNoTracking().SingleAsync(Ct)).ProtectedPayload.ShouldBe(envelope);
    }

    [Fact]
    public async Task Actual_Host_concurrent_last_slot_keeps_store_owned_capacity_transaction_atomic()
    {
        await using var fixture = new TripsHostFixture();
        await fixture.StartAsync(Ct);
        await using (var db = fixture.Db())
        {
            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < 99; index++)
                db.Plans.Add(
                    new TripPlanEntity
                    {
                        TripId = Guid.NewGuid(),
                        OwnerIssuer = TripsHostFixture.Issuer,
                        OwnerSubject = TripsHostFixture.Owner,
                        Revision = 1,
                        CreatedAt = now,
                        UpdatedAt = now,
                        ProtectedPayload = "fictional capacity-only sentinel",
                    }
                );
            await db.SaveChangesAsync(Ct);
        }
        var responses = await Task.WhenAll(
            fixture.Client.PostAsJsonAsync("/api/trips", CreateBody, Ct),
            fixture.Client.PostAsJsonAsync("/api/trips", CreateBody, Ct)
        );
        try
        {
            responses.Count(x => x.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
            var refused = responses.Single(x => x.StatusCode == HttpStatusCode.Conflict);
            JsonNode.Parse(await refused.Content.ReadAsStringAsync(Ct))!["code"]!
                .GetValue<string>()
                .ShouldBe("Trips.CapacityExceeded");
            await using var verify = fixture.Db();
            (await verify.Plans.CountAsync(Ct)).ShouldBe(100);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    private static HttpRequestMessage Write(string id, string revision, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/trips/" + id)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"" + revision + "\"");
        return request;
    }
}
