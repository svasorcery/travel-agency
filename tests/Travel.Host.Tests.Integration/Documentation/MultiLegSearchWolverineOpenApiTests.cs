using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Host.Tests.Integration.Flights;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Documentation;

public sealed class MultiLegSearchWolverineOpenApiTests
{
    [Fact]
    public async Task Production_Wolverine_v2_handler_compiles_and_executes_with_only_fake_bus()
    {
        await using var app = await SavedTravelersWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        var bus = app.Services.GetRequiredService<FakeMessageBus>();
        bus.OnCapture<SearchFlightsQuery>(query =>
        {
            query.Criteria.Legs.Count.ShouldBe(2);
            query.Criteria.Legs[1].Origin.Value.ShouldBe("LHR");
            return (ErrorOr<SearchResult>)new SearchResult([], []);
        });
        using var response = await app.GetTestClient()
            .PostAsJsonAsync(
                "/api/flights/search/v2",
                new
                {
                    legs = new[]
                    {
                        new
                        {
                            origin = "LED",
                            destination = "DME",
                            departureDate = "2030-06-10",
                        },
                        new
                        {
                            origin = "LHR",
                            destination = "CDG",
                            departureDate = "2030-06-11",
                        },
                    },
                },
                TestContext.Current.CancellationToken
            );
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        bus.InvocationCount.ShouldBe(1);
        bus.Reset();
        using var invalid = await app.GetTestClient()
            .PostAsJsonAsync(
                "/api/flights/search/v2",
                new { legs = Array.Empty<object>() },
                TestContext.Current.CancellationToken
            );
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        bus.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Production_v2_discovery_documents_actual_request_response_schemas_and_global_tag()
    {
        await using var app = await SavedTravelersWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        var route = app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .SingleOrDefault(r => r.RoutePattern.RawText == "/api/flights/search/v2");
        route.ShouldNotBeNull();
        route.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
        var body = await app.GetTestClient()
            .GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var operation = root.GetProperty("paths")
            .GetProperty("/api/flights/search/v2")
            .GetProperty("post");
        operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()
            .ShouldBe("#/components/schemas/SearchRequestV2");
        operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()
            .ShouldBe("#/components/schemas/SearchResponse");
        var schemas = root.GetProperty("components").GetProperty("schemas");
        schemas
            .GetProperty("SearchRequestV2")
            .GetProperty("properties")
            .GetProperty("legs")
            .GetProperty("items")
            .GetProperty("$ref")
            .GetString()
            .ShouldBe("#/components/schemas/RequestedFlightLegDto");
        schemas
            .GetProperty("ItineraryDto")
            .GetProperty("properties")
            .TryGetProperty("journeyKind", out _)
            .ShouldBeTrue();
        var tag = "Travel.Modules.Flights.Api.Endpoints.MultiLegSearchEndpoint";
        root.GetProperty("tags")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ShouldContain(tag);
        var expected = JsonNode.Parse(File.ReadAllText(SnapshotPath()))!;
        JsonNode
            .DeepEquals(
                expected["paths"]!["/api/flights/search/v2"],
                JsonNode.Parse(
                    root.GetProperty("paths").GetProperty("/api/flights/search/v2").GetRawText()
                )
            )
            .ShouldBeTrue("Host snapshot must contain the observed production route.");
        // Compare every schema reachable from the search contract. The shared fixture
        // also exposes cancellation metadata, which has a separate contract test.
        var searchSchemas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rootSchema in new[] { "SearchRequestV2", "SearchResponse" })
        {
            searchSchemas.Add(rootSchema);
            CollectSchemaReferences(schemas.GetProperty(rootSchema), schemas, searchSchemas);
        }
        foreach (var schema in schemas.EnumerateObject().Where(s => searchSchemas.Contains(s.Name)))
            JsonNode
                .DeepEquals(
                    expected["components"]!["schemas"]![schema.Name],
                    JsonNode.Parse(schema.Value.GetRawText())
                )
                .ShouldBeTrue("Observed schema differs: " + schema.Name);
        expected["tags"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ShouldContain(tag);
    }

    private static void CollectSchemaReferences(
        JsonElement node,
        JsonElement schemas,
        HashSet<string> collected
    )
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                CollectSchemaReferences(child, schemas, collected);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object)
            return;
        if (
            node.TryGetProperty("$ref", out var reference)
            && reference.GetString() is { } value
            && value.StartsWith("#/components/schemas/", StringComparison.Ordinal)
        )
        {
            var name = value["#/components/schemas/".Length..];
            if (collected.Add(name))
                CollectSchemaReferences(schemas.GetProperty(name), schemas, collected);
        }
        foreach (var property in node.EnumerateObject())
            CollectSchemaReferences(property.Value, schemas, collected);
    }

    private static string SnapshotPath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..",
            "Web",
            "Verified",
            "HostWebContractTests.OpenApi.verified.txt"
        );
}
