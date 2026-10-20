using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Composition;

public sealed record TripsFeature(bool Enabled);

public static class TripsModule
{
    public static IHostApplicationBuilder AddTripsModule(this IHostApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue<bool>("Trips:Enabled");
        builder.Services.AddSingleton(new TripsFeature(enabled));
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
