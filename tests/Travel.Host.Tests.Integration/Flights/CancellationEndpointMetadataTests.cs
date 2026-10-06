using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Host.Tests.Integration.Documentation;
using Travel.Modules.Flights.Api.Endpoints;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class CancellationEndpointMetadataTests
{
    [Fact]
    public async Task Production_Wolverine_cancellation_routes_publish_scope_body_and_financial_provenance_metadata()
    {
        await using var app = await SavedTravelersWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        var expected = new Dictionary<string, (string Method, string Policy)>
        {
            ["/api/flights/orders/{aggregateId}/cancellation"] = ("get", "flights:book"),
            ["/api/flights/orders/{aggregateId}/cancellation-review"] = (
                "get",
                "flights:cancellation-review"
            ),
            ["/api/flights/cancellations/prepare"] = ("post", "flights:book"),
            ["/api/flights/cancellations/consent"] = ("post", "flights:book"),
            ["/api/flights/cancellations/abandon"] = ("post", "flights:book"),
            ["/api/flights/cancellations/refresh"] = ("post", "flights:book"),
            ["/api/flights/cancellations/review/refresh"] = ("post", "flights:cancellation-review"),
            ["/api/flights/cancellations/review/resolve"] = ("post", "flights:cancellation-review"),
        };
        expected.Keys.ShouldBe(CancellationApiFixture.FixtureRoutePaths, ignoreOrder: true);
        var routes = app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(r => expected.ContainsKey(r.RoutePattern.RawText ?? ""))
            .ToArray();
        routes.Length.ShouldBe(8);
        foreach (var route in routes)
            route
                .Metadata.OfType<IAuthorizeData>()
                .ShouldContain(a => a.Policy == expected[route.RoutePattern.RawText!].Policy);

        var body = await app.GetTestClient()
            .GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var paths = json.RootElement.GetProperty("paths");
        foreach (var (path, contract) in expected)
        {
            var endpoint = paths.GetProperty(path).GetProperty(contract.Method);
            endpoint
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()!
                .ShouldContain(
                    path.EndsWith("cancellation-review", StringComparison.Ordinal)
                        ? "CancellationReviewResponse"
                        : "CancellationStatusResponse"
                );
            if (contract.Method == "post")
                endpoint
                    .GetProperty("requestBody")
                    .GetProperty("required")
                    .GetBoolean()
                    .ShouldBeTrue();
        }
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        var financial = schemas.GetProperty("CancellationTermsResponse");
        financial
            .GetProperty("required")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ShouldContain("financialSource");
        financial
            .GetProperty("properties")
            .GetProperty("financialSource")
            .GetProperty("type")
            .GetString()
            .ShouldBe("string");
        var owner = schemas.GetProperty("CancellationStatusResponse").GetRawText();
        owner.ShouldNotContain("providerOrderRef");
        owner.ShouldNotContain("dispatchInstanceId");
        owner.ShouldNotContain("operatorActor");
        foreach (var path in new[] { "prepare", "consent", "refresh", "review/refresh" })
            paths
                .GetProperty("/api/flights/cancellations/" + path)
                .GetProperty("post")
                .GetProperty("responses")
                .GetProperty("202")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString()!
                .ShouldContain("CancellationStatusResponse");
    }
}
