using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Travel.Modules.Hotels.Api.Composition;
using Travel.Modules.Hotels.Api.Endpoints;
using Travel.Modules.Hotels.Application;
using Wolverine;
using Wolverine.Http;
using Xunit;

namespace Travel.Host.Tests.Integration.Hotels;

public sealed class HotelsWolverineOpenApiTests
{
    [Fact]
    public async Task Actual_Wolverine_OpenApi_exposes_public_v2_contract_without_private_selection_or_admission_material()
    {
        // Schema-only standalone application: no Host Program, module registration,
        // database, schema initializer, certificates or provider transport.
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ApplicationName = typeof(HotelsWolverineOpenApiTests).Assembly.FullName,
            }
        );
        builder.Configuration.Sources.Clear();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi();
        builder.Services.AddAuthentication();
        HotelsModule.ConfigureAuthorization(builder.Services);
        builder.Services.AddScoped<HotelSearchService>(_ =>
            throw new InvalidOperationException("Schema-only fixture must not invoke handlers.")
        );
        builder.Services.AddScoped<HotelReservationService>(_ =>
            throw new InvalidOperationException("Schema-only fixture must not invoke handlers.")
        );
        builder.Services.AddWolverine(o =>
        {
            o.ApplicationAssembly = typeof(HotelEndpoints).Assembly;
            o.Discovery.DisableConventionalDiscovery();
            HotelsModule.ConfigureWolverine(o);
            o.Durability.Mode = DurabilityMode.MediatorOnly;
            o.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        });
        builder.Services.AddWolverineHttp();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi().AllowAnonymous();
        app.MapWolverineEndpoints(o => o.WarmUpRoutes = RouteWarmup.Lazy);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var json = await client.GetStringAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken
        );
        foreach (
            var field in new[]
            {
                "propertyGroupKey",
                "schemaVersion",
                "rankingPolicyVersion",
                "groups",
                "attempted",
                "resultCount",
                "acceptedPrice",
                "acceptedConditions",
            }
        )
            json.ShouldContain('"' + field + '"');
        foreach (
            var field in new[]
            {
                "selectionContext",
                "admissionBinding",
                "mappingEvidenceDigest",
                "transportCredentialFingerprint",
                "reviewer",
                "evidence",
            }
        )
            json.ShouldNotContain('"' + field + '"');
        json.ShouldContain("/api/hotels/reservations/{id}/quote");
        json.ShouldContain("/api/hotels/rates");
    }
}
