using System.Text.Json.Nodes;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Rail.Api.Composition;
using Travel.Modules.Rail.Api.Endpoints;
using Wolverine;
using Wolverine.Http;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class JourneyOpenApiTests
{
    [Fact]
    public async Task Lean_Rail_Wolverine_OpenAPI_describes_details_without_Host_or_database()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        builder.AddRailModule();
        builder.Services.AddAuthorization();
        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(RailEndpoints).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            RailModule.ConfigureWolverine(options);
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        });
        builder.Services.AddWolverineHttp();
        await using var app = builder.Build();
        app.UseAuthorization();
        app.UseRailResponsePolicy();
        app.MapOpenApi().AllowAnonymous();
        app.MapWolverineEndpoints(options => options.WarmUpRoutes = RouteWarmup.Lazy);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!;
        var details = json["paths"]!["/api/rail/journey-details"]!["post"]!;
        details["requestBody"]!["required"]!.GetValue<bool>().ShouldBeTrue();
        foreach (var status in new[] { "200", "400", "413", "429", "500" })
            details["responses"]![status]!["headers"]!["Cache-Control"]!["schema"]!["const"]!
                .GetValue<string>()
                .ShouldBe("no-store");
        // Source snapshot preparation only; this fixture never loads Travel.Host/Program.cs.
        if (Environment.GetEnvironmentVariable("RAIL_M2_OPENAPI_OUTPUT") is { } output)
            await File.WriteAllTextAsync(
                output,
                json.ToJsonString(
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                ),
                TestContext.Current.CancellationToken
            );
    }
}
