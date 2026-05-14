using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Travel.Host.Persistence;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.Marten;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Identity.Infrastructure;
using Travel.Shared.Infrastructure.Initialization;
using Travel.Shared.Web;
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

// All Flights provider adapters, caches, persistence-backed stores, notifications and the
// Keycloak admin integration — see FlightsModuleServiceCollectionExtensions.
builder.Services.AddFlightsModule(builder.Configuration, builder.Environment);

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

    // The Flights handlers and HTTP endpoints live outside the Travel.Host entry assembly,
    // so Wolverine must scan their assemblies for [WolverineHandler] / [WolverinePost] /
    // [WolverineGet] discovery (Application handlers, Infrastructure background jobs, Api endpoints).
    opts.Discovery.IncludeAssembly(
        typeof(Travel.Modules.Flights.Application.Handlers.Search.SearchFlightsHandler).Assembly
    );
    opts.Discovery.IncludeAssembly(typeof(FlightsModuleServiceCollectionExtensions).Assembly);
    opts.Discovery.IncludeAssembly(
        typeof(Travel.Modules.Flights.Api.Endpoints.SearchEndpoint).Assembly
    );
});

builder.Services.AddWolverineHttp(); // required for MapWolverineEndpoints() to function

builder.Services.AddAppInitialization(); // hosted service that runs IInitializer impls at startup

// Prevent [TestOnly] types (e.g. DuffelTestWalletPaymentGateway) from being
// registered in Production. Throws InvalidOperationException if any violation is found.
TestOnlyGuard.Verify(builder.Services, builder.Environment);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<IdempotencyKeyMiddleware>();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints(); // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();

public partial class Program;
