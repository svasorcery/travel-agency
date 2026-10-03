using System.Net;
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

public sealed class FlightsBookingOpenApiTests
{
    [Fact]
    public async Task Booking_contract_schemas_generate_and_match_Host_snapshot_without_database()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(FlightsBookingOpenApiTests).Assembly.FullName,
                EnvironmentName = Environments.Development,
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        // Same OpenAPI registration and default HTTP JSON options as Travel.Host.
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/booking-contract", (HoldOfferRequest request) => request);
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient()
            .GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var hold = schemas.GetProperty("HoldOfferRequest");
        var passenger = schemas.GetProperty("PassengerInfoDto");
        AssertRequiredGuid(hold, "quoteRevision");
        AssertRequiredGuid(passenger, "bookingPassengerId");
        passenger
            .GetProperty("properties")
            .GetProperty("title")
            .GetProperty("default")
            .GetString()
            .ShouldBe(string.Empty);
        passenger
            .GetProperty("required")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ShouldNotContain("title");

        var expected = JsonNode.Parse(File.ReadAllText(SnapshotPath()))!["components"]!["schemas"]!;
        foreach (var schemaName in new[] { "HoldOfferRequest", "PassengerInfoDto" })
        {
            var actual = JsonNode.Parse(schemas.GetProperty(schemaName).GetRawText());
            JsonNode
                .DeepEquals(actual, expected[schemaName])
                .ShouldBeTrue($"Host snapshot differs from generated {schemaName}: {actual}");
        }
    }

    private static void AssertRequiredGuid(JsonElement schema, string propertyName)
    {
        schema
            .GetProperty("required")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ShouldContain(propertyName);
        var property = schema.GetProperty("properties").GetProperty(propertyName);
        property.GetProperty("type").GetString().ShouldBe("string");
        property.GetProperty("format").GetString().ShouldBe("uuid");
        property.TryGetProperty("default", out _).ShouldBeFalse();
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
