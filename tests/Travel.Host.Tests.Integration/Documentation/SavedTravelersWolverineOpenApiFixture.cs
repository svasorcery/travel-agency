using ErrorOr;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Travel.Host.Tests.Integration.Flights;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Identity.Infrastructure.Authentication;
using Wolverine;
using Wolverine.Http;

namespace Travel.Host.Tests.Integration.Documentation;

/// <summary>Profiles, v2 search and cancellation HTTP metadata only: no persistence, initializer, providers, or transports.</summary>
internal static class SavedTravelersWolverineOpenApiFixture
{
    internal static async Task<WebApplication> CreateAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(SavedTravelersWolverineOpenApiFixture).Assembly.FullName,
                EnvironmentName = Environments.Development,
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.AddServiceDefaults();
        builder.Services.AddSingleton<ISavedTravelerService>(new FakeSavedTravelerService());
        builder.Services.AddSingleton<IIdempotencyStore>(new FakeIdempotencyStore());
        builder.Services.AddSingleton<FakeMessageBus>();
        builder.Services.AddTransient<
            IClaimsTransformation,
            NormalizedIdentityClaimsTransformation
        >();
        builder
            .Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { }
            );
        builder.Services.AddAuthorization(options =>
            options.AddPolicy(
                "flights:book",
                policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "flights:book")
            )
        );
        builder.Services.AddAuthorization(options =>
            options.AddPolicy(
                "flights:cancellation-review",
                policy =>
                    policy
                        .RequireAuthenticatedUser()
                        .RequireClaim("scope", "flights:cancellation-review")
            )
        );
        WolverineOptions? registryRules = null;
        builder.Services.AddWolverine(options =>
        {
            registryRules = options;
            options.ApplicationAssembly = typeof(ProfileEndpointRegistry).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<MultiLegSearchMetadataHandler>();
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            options.CodeGeneration.AlwaysUseServiceLocationFor<ISavedTravelerService>();
            options.CodeGeneration.AlwaysUseServiceLocationFor<FakeMessageBus>();
        });
        builder.Services.DisableAllExternalWolverineTransports();
        builder.Services.AddWolverineHttp();
        var app = builder.Build();
        app.UseFlightsResponsePolicy();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFlightsModule();
        app.MapOpenApi().AllowAnonymous();
        app.MapWolverineEndpoints(options =>
        {
            options.WarmUpRoutes = RouteWarmup.Lazy;
            // DiscoverEndpoints consumes the explicit Static registry first, applies policies second,
            // and builds handlers third. Enable ordinary compilation only after those explicit types are fixed.
            options.ConfigureEndpoints(_ =>
                registryRules!.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto
            );
        });
        await app.StartAsync(ct);
        return app;
    }
}

// The installed Wolverine public registry fast path replaces assembly scanning in Static mode.
// Public only for that registry loader; this cannot affect actualHost, whose ApplicationAssembly is Travel.Host.
public sealed class ProfileEndpointRegistry : HttpEndpointRegistry
{
    public override Type[] EndpointTypes() =>
        [
            typeof(DeleteSavedTravelerEndpoint),
            typeof(GetSavedTravelerEndpoint),
            typeof(ListSavedTravelersEndpoint),
            typeof(PutSavedTravelerEndpoint),
            typeof(MultiLegSearchEndpoint),
            typeof(GetCancellationStatusEndpoint),
            typeof(GetCancellationReviewEndpoint),
            typeof(PrepareCancellationEndpoint),
            typeof(ConsentCancellationEndpoint),
            typeof(AbandonCancellationEndpoint),
            typeof(RefreshCancellationEndpoint),
            typeof(RefreshCancellationReviewEndpoint),
            typeof(ResolveCancellationReviewEndpoint),
        ];
}

// Only this controlled handler is explicitly included; production search handlers cannot run here.
public sealed class MultiLegSearchMetadataHandler
{
    public static Task<ErrorOr<SearchResult>> Handle(
        SearchFlightsQuery query,
        FakeMessageBus bus
    ) => bus.InvokeAsync<ErrorOr<SearchResult>>(query);
}
