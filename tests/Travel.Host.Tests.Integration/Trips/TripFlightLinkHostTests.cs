using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripFlightLinkHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenApi_and_actual_collection_agree_on_PATCH_body_precondition_and_response()
    {
        await using var host = new TripsHostFixture(flightLinks: true);
        await host.StartAsync(Ct);
        using var api = await host.Client.GetAsync("/openapi/v1.json", Ct);
        api.EnsureSuccessStatusCode();
        using var contract = JsonDocument.Parse(await api.Content.ReadAsStringAsync(Ct));
        var paths = contract.RootElement.GetProperty("paths");
        foreach (
            var existing in new[]
            {
                "/api/trips",
                "/api/flights/search",
                "/api/support/operator/cases",
            }
        )
            paths.TryGetProperty(existing, out _).ShouldBeTrue(existing);
        var collection = paths.GetProperty("/api/trips/{tripId}/flight-links");
        var patch = collection.GetProperty("patch");
        patch
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ShouldContain("If-Match", string.Join(", ", host.LinkRouteDescriptions()));
        patch
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "If-Match")
            .GetProperty("required")
            .GetBoolean()
            .ShouldBeTrue();
        var bodySchema = patch
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        bodySchema = Resolve(bodySchema);
        bodySchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(v => v.GetString())
            .Order()
            .ShouldBe(new[] { "op", "orderId" });
        var properties = bodySchema.GetProperty("properties");
        properties
            .GetProperty("op")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(v => v.GetString())
            .Order()
            .ShouldBe(new[] { "add", "remove" });
        properties.GetProperty("orderId").GetProperty("format").GetString().ShouldBe("uuid");
        bodySchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        patch
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("headers")
            .TryGetProperty("ETag", out _)
            .ShouldBeTrue();
        var getMetadata = collection.GetProperty("get");
        getMetadata
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("headers")
            .TryGetProperty("ETag", out _)
            .ShouldBeTrue();
        var metadata = ResponseProperties(getMetadata);
        Names(metadata).ShouldBe(new[] { "links", "linksRevision", "tripId" });
        Names(ResponseProperties(patch)).ShouldBe(Names(metadata));
        metadata.GetProperty("linksRevision").GetProperty("type").GetString().ShouldBe("string");
        Names(Resolve(metadata.GetProperty("links").GetProperty("items")).GetProperty("properties"))
            .ShouldBe(new[] { "linkedAt", "orderId" });
        var availability = ResponseProperties(
            paths.GetProperty("/api/trips/flight-links/availability").GetProperty("get")
        );
        Names(availability).ShouldBe(new[] { "enabled" });
        var status = ResponseProperties(
            paths.GetProperty("/api/trips/{tripId}/flight-links/status").GetProperty("get")
        );
        Names(status).ShouldBe(new[] { "items", "linksRevision", "tripId" });
        var statusFields = new HashSet<string>(StringComparer.Ordinal);
        CollectSchemaFields(status.GetProperty("items").GetProperty("items"), statusFields, []);
        foreach (
            var field in new[]
            {
                "orderId",
                "state",
                "bookingStatus",
                "reviewRequired",
                "route",
                "passengerCount",
                "observedAt",
                "streamVersion",
            }
        )
            statusFields.ShouldContain(
                field,
                "Status item field missing from generated Host schema."
            );
        var candidateOperation = paths
            .GetProperty("/api/trips/{tripId}/flight-order-candidates")
            .GetProperty("get");
        var candidate = ResponseProperties(candidateOperation);
        Names(candidate).ShouldBe(new[] { "items", "limitReached", "nextOffset", "tripId" });
        Names(
                Resolve(candidate.GetProperty("items").GetProperty("items"))
                    .GetProperty("properties")
            )
            .ShouldBe(new[] { "bookingStatus", "orderId", "passengerCount", "route" });
        var offset = candidateOperation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "offset");
        offset.GetProperty("schema").GetProperty("maximum").GetInt32().ShouldBe(10000);
        using var plan = await host.Client.PostAsJsonAsync(
            "/api/trips",
            new
            {
                title = "Fictional trip",
                brief = new
                {
                    destinationId = "moscow",
                    originId = (string?)null,
                    startDate = "2026-11-18",
                    endDate = "2026-11-19",
                    adults = 1,
                    currency = "RUB",
                    totalBudget = (string?)null,
                    interests = new[] { "architecture" },
                    pace = "balanced",
                },
            },
            Ct
        );
        plan.StatusCode.ShouldBe(HttpStatusCode.Created);
        var path = plan.Headers.Location!.OriginalString + "/flight-links";
        using var empty = await host.Client.GetAsync(path, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        empty.Headers.ETag!.Tag.ShouldBe("\"fl-0\"");
        var emptyJson = await empty.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Names(emptyJson).ShouldBe(Names(metadata));
        emptyJson.GetProperty("linksRevision").GetString().ShouldBe("0");
        using var initialStatus = await host.Client.GetAsync(path + "/status", Ct);
        initialStatus.EnsureSuccessStatusCode();
        Names(await initialStatus.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .ShouldBe(Names(status));
        using var picker = await host.Client.GetAsync(
            plan.Headers.Location!.OriginalString + "/flight-order-candidates",
            Ct
        );
        picker.EnsureSuccessStatusCode();
        var pickerJson = await picker.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Names(pickerJson).ShouldBe(Names(candidate));
        pickerJson.GetProperty("items").GetArrayLength().ShouldBe(0);
        using var add = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(new { op = "add", orderId = Guid.NewGuid() }),
        };
        add.Headers.IfMatch.Add(empty.Headers.ETag!);
        using var missingSource = await host.Client.SendAsync(add, Ct);
        missingSource.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var remove = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(new { op = "remove", orderId = Guid.NewGuid() }),
        };
        remove.Headers.IfMatch.Add(empty.Headers.ETag!);
        using var unchanged = await host.Client.SendAsync(remove, Ct);
        unchanged.StatusCode.ShouldBe(HttpStatusCode.OK);
        unchanged.Headers.ETag.ShouldBe(empty.Headers.ETag);
        Names(await unchanged.Content.ReadFromJsonAsync<JsonElement>(Ct)).ShouldBe(Names(metadata));
        using var foreign = new HttpRequestMessage(HttpMethod.Get, path);
        host.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        foreign.Headers.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        using var hidden = await host.Client.SendAsync(foreign, Ct);
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        JsonElement Resolve(JsonElement schema) =>
            schema.TryGetProperty("$ref", out var reference)
                ? contract
                    .RootElement.GetProperty("components")
                    .GetProperty("schemas")
                    .GetProperty(reference.GetString()!.Split('/')[^1])
                : schema;
        void CollectSchemaFields(JsonElement schema, HashSet<string> fields, HashSet<string> seen)
        {
            if (
                schema.TryGetProperty("$ref", out var reference)
                && !seen.Add(reference.GetString()!)
            )
                return;
            schema = Resolve(schema);
            if (schema.TryGetProperty("properties", out var props))
                fields.UnionWith(Names(props));
            foreach (var union in new[] { "oneOf", "anyOf", "allOf" })
                if (schema.TryGetProperty(union, out var variants))
                    foreach (var variant in variants.EnumerateArray())
                        CollectSchemaFields(variant, fields, seen);
        }
        JsonElement ResponseProperties(JsonElement operation) =>
            Resolve(
                    operation
                        .GetProperty("responses")
                        .GetProperty("200")
                        .GetProperty("content")
                        .GetProperty("application/json")
                        .GetProperty("schema")
                )
                .GetProperty("properties");
        static string[] Names(JsonElement element) =>
            element.EnumerateObject().Select(v => v.Name).Order().ToArray();
    }
}
