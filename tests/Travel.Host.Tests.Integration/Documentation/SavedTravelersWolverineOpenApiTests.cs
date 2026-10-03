using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.Documentation;

public sealed class SavedTravelersWolverineOpenApiTests
{
    [Fact]
    public async Task Four_production_profile_endpoints_have_actual_Wolverine_routes_headers_and_schema_metadata()
    {
        await using var app = await SavedTravelersWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        var routes = app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(r =>
                r.RoutePattern.RawText?.StartsWith(
                    "/api/flights/travelers",
                    StringComparison.Ordinal
                ) == true
            )
            .ToList();
        routes.Count.ShouldBe(4);
        foreach (var route in routes)
            route.Metadata.OfType<IAuthorizeData>().ShouldContain(a => a.Policy == "flights:book");
        var body = await app.GetTestClient()
            .GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var paths = json.RootElement.GetProperty("paths");
        paths
            .EnumerateObject()
            .Count(p => p.Name.StartsWith("/api/flights/travelers", StringComparison.Ordinal))
            .ShouldBe(2);
        var item = paths.GetProperty("/api/flights/travelers/{travelerId}");
        foreach (var method in new[] { "put", "delete" })
            item.GetProperty(method)
                .GetProperty("parameters")
                .EnumerateArray()
                .Where(p => p.GetProperty("in").GetString() == "header")
                .Select(p => p.GetProperty("name").GetString())
                .ShouldBe(["If-Match", "If-None-Match"], ignoreOrder: true);
        item.GetProperty("delete")
            .GetProperty("responses")
            .TryGetProperty("200", out _)
            .ShouldBeFalse(
                "DELETE must document the exact 204 success, not a phantom 200 response"
            );
        var expected = JsonNode.Parse(File.ReadAllText(SnapshotPath()))!;
        var actualTags = json
            .RootElement.GetProperty("tags")
            .EnumerateArray()
            .Select(tag => tag.GetProperty("name").GetString())
            .ToArray();
        actualTags.ShouldBe(
            new[]
            {
                typeof(Travel.Modules.Flights.Api.Endpoints.DeleteSavedTravelerEndpoint).FullName,
                typeof(Travel.Modules.Flights.Api.Endpoints.GetSavedTravelerEndpoint).FullName,
                typeof(Travel.Modules.Flights.Api.Endpoints.PutSavedTravelerEndpoint).FullName,
                typeof(Travel.Modules.Flights.Api.Endpoints.ListSavedTravelersEndpoint).FullName,
                typeof(Travel.Modules.Flights.Api.Endpoints.MultiLegSearchEndpoint).FullName,
            },
            ignoreOrder: true
        );
        var expectedTags = expected["tags"]!
            .AsArray()
            .Select(tag => tag!["name"]!.GetValue<string>())
            .ToArray();
        foreach (var tag in actualTags)
            expectedTags.ShouldContain(tag);

        foreach (
            var path in paths
                .EnumerateObject()
                .Where(p => p.Name.StartsWith("/api/flights/travelers", StringComparison.Ordinal))
        )
            JsonNode
                .DeepEquals(JsonNode.Parse(path.Value.GetRawText()), expected["paths"]![path.Name])
                .ShouldBeTrue(
                    "Host snapshot differs from actual profile route: "
                        + path.Name
                        + " "
                        + path.Value.GetRawText()
                );
    }

    [Fact]
    public async Task Four_production_Wolverine_handlers_compile_and_execute_with_only_controlled_local_services()
    {
        await using var app = await SavedTravelersWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        var client = app.GetTestClient();
        const string path = "/api/flights/travelers/11111111-1111-1111-1111-111111111111";
        var service = (Travel.Host.Tests.Integration.Flights.FakeSavedTravelerService)
            app.Services.GetRequiredService<Travel.Modules.Flights.Application.SavedTravelers.ISavedTravelerService>();
        foreach (var method in new[] { "GET", "PUT", "DELETE" })
        {
            service.Reset();
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add(
                Travel.Host.Tests.Integration.Flights.TestAuthHandler.UserIdHeader,
                "22222222-2222-2222-2222-222222222222"
            );
            request.Headers.Add(
                Travel.Host.Tests.Integration.Flights.TestAuthHandler.ScopesHeader,
                "flights:book"
            );
            request.Headers.Add("If-Match", "\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"");
            if (method == "PUT")
                request.Content = new StringContent(
                    "{\"title\":\"mr\",\"givenName\":\"John\",\"familyName\":\"Smith\",\"dateOfBirth\":\"1990-01-01\",\"gender\":\"male\",\"email\":\"john@example.test\",\"phone\":\"+12025550123\"}",
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
            using var response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            ((int)response.StatusCode).ShouldBe(
                method == "DELETE" ? 204 : 200,
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
            (response.Headers.CacheControl?.NoStore).ShouldBe(true);
            service.Owner.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
            service.Calls.ShouldBe(1);
        }
        service.Reset();
        using var invalidOffset = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/flights/travelers?offset=bad"
        );
        invalidOffset.Headers.Add(
            Travel.Host.Tests.Integration.Flights.TestAuthHandler.UserIdHeader,
            "22222222-2222-2222-2222-222222222222"
        );
        invalidOffset.Headers.Add(
            Travel.Host.Tests.Integration.Flights.TestAuthHandler.ScopesHeader,
            "flights:book"
        );
        using var invalid = await client.SendAsync(
            invalidOffset,
            TestContext.Current.CancellationToken
        );
        ((int)invalid.StatusCode).ShouldBe(400);
        service.Calls.ShouldBe(0);
        using var validOffset = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/flights/travelers?offset=20"
        );
        validOffset.Headers.Add(
            Travel.Host.Tests.Integration.Flights.TestAuthHandler.UserIdHeader,
            "22222222-2222-2222-2222-222222222222"
        );
        validOffset.Headers.Add(
            Travel.Host.Tests.Integration.Flights.TestAuthHandler.ScopesHeader,
            "flights:book"
        );
        using var valid = await client.SendAsync(
            validOffset,
            TestContext.Current.CancellationToken
        );
        ((int)valid.StatusCode).ShouldBe(200);
        service.Calls.ShouldBe(1);
        (
            (Travel.Host.Tests.Integration.Flights.FakeIdempotencyStore)
                app.Services.GetRequiredService<Travel.Modules.Flights.Application.Idempotency.IIdempotencyStore>()
        ).TryBeginCount.ShouldBe(0);
    }

    private static string SnapshotPath([CallerFilePath] string sourceFile = "") =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(sourceFile)!,
            "..",
            "Web",
            "Verified",
            "HostWebContractTests.OpenApi.verified.txt"
        );
}
