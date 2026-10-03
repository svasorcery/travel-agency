using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Xunit;

namespace Travel.Host.Tests.Integration.Documentation;

public sealed class SavedTravelersOpenApiTests
{
    [Fact]
    public async Task Profile_schemas_have_required_fields_and_Guids_without_default_and_match_Host_snapshot()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapGet("/api/flights/travelers", () => new SavedTravelerPageDto([], 0, false));
        app.MapGet(
            "/api/flights/travelers/{travelerId:guid}",
            (Guid travelerId) => new SavedTravelerViewDto(travelerId, Guid.NewGuid(), null!)
        );
        app.MapPut(
            "/api/flights/travelers/{travelerId:guid}",
            (Guid travelerId, SavedTravelerDetailsDto details) =>
                new SavedTravelerReceiptDto(travelerId, Guid.NewGuid())
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        var body = await app.GetTestClient()
            .GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var schemas = json.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var type in new[] { "SavedTravelerViewDto", "SavedTravelerReceiptDto" })
        {
            var schema = schemas.GetProperty(type);
            foreach (var name in new[] { "id", "revision" })
            {
                schema
                    .GetProperty("required")
                    .EnumerateArray()
                    .Select(p => p.GetString())
                    .ShouldContain(name);
                var property = schema.GetProperty("properties").GetProperty(name);
                property.GetProperty("format").GetString().ShouldBe("uuid");
                property.TryGetProperty("default", out _).ShouldBeFalse();
            }
        }
        var details = schemas.GetProperty("SavedTravelerDetailsDto");
        details
            .GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)
            .ShouldBe([
                "title",
                "givenName",
                "familyName",
                "dateOfBirth",
                "gender",
                "email",
                "phone",
            ]);
        details.GetProperty("required").EnumerateArray().Count().ShouldBe(7);
        details.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        var expected = JsonNode.Parse(File.ReadAllText(SnapshotPath()))!["components"]!["schemas"]!;
        foreach (
            var name in new[]
            {
                "SavedTravelerViewDto",
                "SavedTravelerReceiptDto",
                "SavedTravelerDetailsDto",
                "SavedTravelerPageDto",
            }
        )
        {
            var actual = JsonNode.Parse(schemas.GetProperty(name).GetRawText());
            JsonNode
                .DeepEquals(actual, expected[name])
                .ShouldBeTrue($"Host snapshot differs from generated {name}: {actual}");
        }
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
