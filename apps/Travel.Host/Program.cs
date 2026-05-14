using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Travel.Host.Persistence;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Infrastructure.Marten;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Identity.Infrastructure;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register Flights OTel meter so the Aspire/OTLP exporter picks it up.
builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(FlightsMetrics.MeterName));

builder.AddNpgsqlDbContext<HostDbContext>(
    "travel",
    configureDbContextOptions: opts =>
    {
        opts.UseSnakeCaseNamingConvention(); // PostgreSQL convention via EFCore.NamingConventions package
    }
);

builder.AddNpgsqlDbContext<FlightsDbContext>(
    "travel",
    configureDbContextOptions: opts =>
    {
        opts.UseSnakeCaseNamingConvention();
    }
);

builder
    .Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("travel")!);
        opts.ConfigureFlightsBooking();
    })
    .UseLightweightSessions();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Flights metrics — single instance shared across all three registrations.
builder.Services.AddSingleton<FlightsMetrics>();
builder.Services.AddSingleton<ISearchMetrics>(sp => sp.GetRequiredService<FlightsMetrics>());
builder.Services.AddSingleton<IFlightsMetrics>(sp => sp.GetRequiredService<FlightsMetrics>());

builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);

// Require authentication by default; individual endpoints can opt out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// Wolverine: wire NATS transport so NlSearchRequested can be dispatched to Travel.AI
// via bus.InvokeAsync<NlSearchParsed>(req, ct, timeout) (Wolverine request/reply over NATS).
var natsUrl = builder.Configuration.GetConnectionString("nats") ?? "nats://localhost:4222";
builder.Host.UseWolverine(opts =>
{
    opts.UseNats(natsUrl);

    // Route NlSearchRequested to Travel.AI listener subject
    opts.PublishMessage<Travel.Modules.Flights.Application.Contracts.NlSearchRequested>()
        .ToNatsSubject("travel.ai.nl_search");
});

builder.Services.AddWolverineHttp(); // required for MapWolverineEndpoints() to function

builder.Services.AddAppInitialization(); // hosted service that runs IInitializer impls at startup

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyKeyMiddleware>();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints(); // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();

public partial class Program;
