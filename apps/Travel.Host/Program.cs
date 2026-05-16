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
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Marten;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Register Flights OTel meter and activity source so the Aspire/OTLP exporter picks them up.
builder
    .Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(FlightsMetrics.MeterName))
    .WithTracing(t => t.AddSource(FlightsActivitySource.Name));

// DisableRetry: Wolverine's transactional-outbox middleware (AutoApplyTransactions +
// UseEntityFrameworkCoreTransactions, configured below) manages the DbContext transaction
// itself. Npgsql's retrying execution strategy (Aspire's default) rejects user-initiated
// transactions, so it must be turned off on every context Wolverine wraps.
builder.AddNpgsqlDbContext<HostDbContext>(
    "travel",
    configureSettings: settings => settings.DisableRetry = true,
    configureDbContextOptions: opts =>
    {
        opts.UseSnakeCaseNamingConvention(); // PostgreSQL convention via EFCore.NamingConventions package
    }
);

builder.AddNpgsqlDbContext<FlightsDbContext>(
    "travel",
    configureSettings: settings => settings.DisableRetry = true,
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
    .UseLightweightSessions()
    // Enrol the Marten session as a Wolverine transactional outbox: appending events
    // and enqueueing outgoing messages now commit atomically in one SaveChangesAsync.
    // This also provisions Wolverine's durable message store in the same Postgres DB.
    .IntegrateWithWolverine();

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// All Flights provider adapters, caches, persistence-backed stores, notifications and the
// Keycloak admin integration — see FlightsModuleServiceCollectionExtensions.
builder.Services.AddFlightsModule(builder.Configuration, builder.Environment);

builder.Services.AddIdentityModule(builder.Configuration, builder.Environment);

// Require authentication by default; individual endpoints can opt out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    // flights:book scope — required on hold / confirm / cancel endpoints.
    // Keycloak emits the scope as a space-delimited string in the "scope" claim;
    // some configurations use "scp" instead. Both are checked.
    options.AddPolicy(
        "flights:book",
        p =>
            p.RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                {
                    var scopeClaim =
                        ctx.User.FindFirst("scope")?.Value ?? ctx.User.FindFirst("scp")?.Value;
                    return scopeClaim?.Split(' ').Contains("flights:book") == true;
                })
    );
});

// Wolverine: wire NATS transport so NlSearchRequested can be dispatched to Travel.AI
// via bus.InvokeAsync<NlSearchParsed>(req, ct, timeout) (Wolverine request/reply over NATS).
var natsUrl = builder.Configuration.GetConnectionString("nats") ?? "nats://localhost:4222";
builder.Host.UseWolverine(opts =>
{
    opts.UseNats(natsUrl);

    // Transactional-outbox policies: every handler runs inside a store transaction and
    // local queues are durable, so persist + publish commit together and survive a crash.
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();

    // Make Wolverine aware of FlightsDbContext so a [Transactional]-marked handler/endpoint
    // taking FlightsDbContext commits the DbContext save and the outbox message together.
    // FlightsDbContext is already registered above via Aspire's AddNpgsqlDbContext<T>; this
    // augments that registration rather than re-registering the context.
    opts.UseEntityFrameworkCoreTransactions();

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
