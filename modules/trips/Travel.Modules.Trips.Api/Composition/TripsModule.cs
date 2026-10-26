using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Composition;

public sealed record TripsFeature(bool Enabled);

public sealed record TripsItineraryFeature(bool Enabled);

public static class TripsModule
{
    public static IHostApplicationBuilder AddTripsModule(this IHostApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue<bool>("Trips:Enabled");
        builder.Services.AddSingleton(new TripsFeature(enabled));
        // The demo source is excluded explicitly: factory descriptors do not fence TestOnly types.
        var itineraryEnabled =
            enabled
            && builder.Configuration.GetValue<bool>("Trips:Itinerary:Enabled")
            && !builder.Environment.IsProduction();
        builder.Services.AddSingleton(new TripsItineraryFeature(itineraryEnabled));
        if (!enabled)
            return builder;
        builder.AddNpgsqlDbContext<TripsDbContext>(
            "travel",
            configureSettings: settings => settings.DisableRetry = true,
            configureDbContextOptions: TripsDbContextConfiguration.Configure
        );
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder
            .Services.AddOptions<TripsProtectionOptions>()
            .Bind(builder.Configuration.GetSection(TripsProtectionOptions.SectionName));
        builder.Services.AddSingleton<TripsProtectionProvider>();
        builder.Services.AddSingleton<ITripPlanCodec, TripPlanJsonCodec>();
        builder.Services.AddSingleton<ITripPlanProtector, DataProtectionTripPlanProtector>();
        builder.Services.AddSingleton<ITripPlanCursor, TripPlanCursor>();
        builder.Services.AddScoped<ITripPlanStore, TripPlanStore>();
        builder.Services.AddScoped<TripPlanService>();
        builder.Services.AddSingleton<ITripsReadiness, TripsReadiness>();
        builder
            .Services.AddHealthChecks()
            .AddCheck<TripsHealthCheck>("trips", tags: ["dependency"]);
        if (itineraryEnabled)
        {
            var withdrawn = builder.Configuration.GetValue<bool>("Trips:Itinerary:Withdrawn");
            builder.Services.AddSingleton<IItineraryFactSource>(services =>
            {
                var clock = services.GetRequiredService<TimeProvider>();
                CuratedItineraryFacts Package(CuratedItineraryRelease release) =>
                    new(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "Trips",
                            "Itineraries",
                            release.DatasetId,
                            release.Version
                        ),
                        clock,
                        () =>
                            withdrawn
                            || builder.Configuration.GetValue<bool>(
                                $"Trips:Itinerary:Destinations:{release.DestinationId}:Withdrawn"
                            ),
                        release
                    );
                return new DestinationItineraryFacts(
                    Package(CuratedItineraryRelease.ReviewedV1),
                    Package(CuratedItineraryRelease.ReviewedSaintPetersburgV1)
                );
            });
            builder.Services.AddSingleton<
                IItineraryCandidateResponseEncoder,
                ItineraryCandidateResponseEncoder
            >();
            builder.Services.AddSingleton<ItineraryAdmissionGate>();
            builder.Services.AddScoped<ItineraryCandidateService>();
        }
        builder.Services.AddWolverineHttp();
        return builder;
    }

    public static void ConfigureWolverine(WolverineOptions options, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Trips:Enabled"))
            return;
        options.Discovery.IncludeAssembly(typeof(TripsDestinationsEndpoint).Assembly);
        options.CodeGeneration.AlwaysUseServiceLocationFor<TripPlanService>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<ITripPlanCodec>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<IItineraryFactSource>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<ItineraryCandidateService>();
    }

    public static WebApplication UseTripsResponsePolicy(this WebApplication app)
    {
        app.UseMiddleware<TripsResponsePolicyMiddleware>();
        return app;
    }

    public static WebApplication UseTripsModule(this WebApplication app)
    {
        app.UseMiddleware<TripsRequestGuardMiddleware>();
        return app;
    }
}
