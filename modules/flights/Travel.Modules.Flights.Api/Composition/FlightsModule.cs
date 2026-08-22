using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Handlers.Search;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.Marten;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Http;
using Wolverine.Nats;

namespace Travel.Modules.Flights.Api.Composition;

public static class FlightsModule
{
    public static IHostApplicationBuilder AddFlightsModule(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<FlightsDbContext>(
            "travel",
            configureSettings: settings => settings.DisableRetry = true,
            configureDbContextOptions: FlightsDbContextConfiguration.Configure
        );

        builder.Services.AddFlightsInfrastructure(builder.Configuration, builder.Environment);

        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(FlightsMetrics.MeterName))
            .WithTracing(tracing => tracing.AddSource(FlightsActivitySource.Name));

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                "flights:book",
                policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "flights:book")
            );
        });

        builder.Services.AddWolverineHttp();
        return builder;
    }

    public static void ConfigureMarten(StoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ConfigureFlightsBooking();
    }

    public static void ConfigureWolverine(WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Discovery.IncludeAssembly(typeof(SearchFlightsHandler).Assembly);
        options.Discovery.IncludeAssembly(
            typeof(FlightsInfrastructureServiceCollectionExtensions).Assembly
        );
        options.Discovery.IncludeAssembly(typeof(SearchEndpoint).Assembly);

        options.PublishMessage<NlSearchRequested>().ToNatsSubject("travel.ai.nl_search");
    }

    public static WebApplication UseFlightsModule(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<IdempotencyKeyMiddleware>();
        return app;
    }
}
