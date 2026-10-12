using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Travel.Modules.Identity.Infrastructure.Authentication;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Endpoints;
using Travel.Modules.Support.Application;
using Wolverine;
using Wolverine.Http;

namespace Travel.Host.Tests.Integration.Support;

/// <summary>
/// Production Support HTTP mapping with only a controlled workflow service and synthetic JWT.
/// No Host Program, schema initializer, database, key ring, provider, or external transport.
/// </summary>
internal static class SupportWolverineOpenApiFixture
{
    internal static async Task<WebApplication> CreateAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(SupportWolverineOpenApiFixture).Assembly.FullName,
                EnvironmentName = Environments.Development,
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<ISupportCaseService>(new SupportHttpService());
        builder.Services.AddTransient<
            IClaimsTransformation,
            NormalizedIdentityClaimsTransformation
        >();
        builder
            .Services.AddAuthentication("SupportWolverineJwt")
            .AddJwtBearer(
                "SupportWolverineJwt",
                options =>
                {
                    options.MapInboundClaims = false;
                    options.IncludeErrorDetails = false;
                    options.TokenValidationParameters = new()
                    {
                        ValidateIssuer = true,
                        ValidIssuer = SupportJwtTestTokens.Issuer,
                        ValidateAudience = true,
                        ValidAudience = SupportJwtTestTokens.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = SupportJwtTestTokens.Key(),
                    };
                }
            );
        SupportModule.ConfigureAuthorization(builder.Services);
        builder.Services.AddWolverine(options =>
        {
            // HttpGraph scans only WolverineOptions.Assemblies in Auto mode. The production
            // Support.Api assembly avoids either loading ProfileEndpointRegistry from this
            // test assembly or discovering unrelated Flights HTTP/message handlers.
            options.ApplicationAssembly = typeof(CreateSupportCaseEndpoint).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            SupportModule.ConfigureWolverine(options);
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        });
        builder.Services.AddWolverineHttp();
        var app = builder.Build();
        app.UseSupportResponsePolicy();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSupportModule();
        app.MapOpenApi().AllowAnonymous();
        app.MapWolverineEndpoints(options => options.WarmUpRoutes = RouteWarmup.Lazy);
        await app.StartAsync(ct);
        return app;
    }
}
