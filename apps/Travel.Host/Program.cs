using System.Net;
using JasperFx;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Travel.Host.Configuration;
using Travel.Host.Persistence;
using Travel.Host.Persistence.Initialization;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Identity.Api.Composition;
using Travel.ServiceDefaults.Health;
using Travel.ServiceDefaults.Hosting;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Marten;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInternalHealthEndpoints(5098);
builder.Services.AddHostConnectionOptions(builder.Configuration, builder.Environment);

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

var postgresHealthEndpoint = GetPostgresEndpoint(
    builder.Configuration.GetConnectionString("travel")
);
var natsHealthEndpoint = GetUriEndpoint(builder.Configuration.GetConnectionString("nats"), 4222);
var redisHealthEndpoint = GetRedisEndpoint(builder.Configuration.GetConnectionString("redis"));
builder.Services.AddRequiredTcpDependencyHealthCheck(
    "postgres",
    postgresHealthEndpoint.Host,
    postgresHealthEndpoint.Port
);
builder.Services.AddRequiredTcpDependencyHealthCheck(
    "nats",
    natsHealthEndpoint.Host,
    natsHealthEndpoint.Port
);
builder.Services.AddRequiredTcpDependencyHealthCheck(
    "redis",
    redisHealthEndpoint.Host,
    redisHealthEndpoint.Port
);

builder
    .Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("travel")!);
        opts.AutoCreateSchemaObjects = AutoCreate.None;
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        FlightsModule.ConfigureMarten(opts);
    })
    .UseLightweightSessions()
    // Enrol the Marten session as a Wolverine transactional outbox: appending events
    // and enqueueing outgoing messages now commit atomically in one SaveChangesAsync.
    // This also provisions Wolverine's durable message store in the same Postgres DB.
    .IntegrateWithWolverine(integration => integration.AutoCreate = AutoCreate.None);

builder.Services.AddInitializer<WolverineMessageStoreInitializer>();

builder.AddIdentityModule();
builder.AddFlightsModule();

// Require authentication by default; individual endpoints can opt out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// Wolverine: wire NATS transport so NlSearchRequested can be dispatched to Travel.AI
// via bus.InvokeAsync<NlSearchParsed>(req, ct, timeout) (Wolverine request/reply over NATS).
var natsUrl = builder.Configuration.GetConnectionString("nats") ?? string.Empty;

// Run schema gates before Wolverine starts durable agents that depend on those schemas.
builder.Services.AddAppInitialization();

builder.Host.UseWolverine(opts =>
{
    opts.ApplicationAssembly = typeof(Program).Assembly;
    opts.AutoBuildMessageStorageOnStartup = AutoCreate.None;

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

    FlightsModule.ConfigureWolverine(opts);
});

// Prevent [TestOnly] types (e.g. DuffelTestWalletPaymentGateway) from being
// registered in Production. Throws InvalidOperationException if any violation is found.
TestOnlyGuard.Verify(builder.Services, builder.Environment);

var app = builder.Build();
app.UsePlatformWebDefaults();

// Validate owner connection options before Marten/Wolverine endpoint materialization can
// consume missing or local Production settings. ValidateOnStart remains the host lifecycle gate.
_ = app.Services.GetRequiredService<IOptions<HealthEndpointOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<HostConnectionOptions>>().Value;
app.Services.GetRequiredService<IStartupValidator>().Validate();

app.UseAuthentication();
app.UseAuthorization();
app.UseFlightsModule();

app.MapDefaultEndpoints();
app.MapWolverineEndpoints(); // discovers endpoint methods via [WolverinePost]/[WolverineGet] attributes

await app.RunAsync();

static (string Host, int Port) GetPostgresEndpoint(string? connectionString)
{
    try
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return (builder.Host ?? string.Empty, builder.Port);
    }
    catch (ArgumentException)
    {
        return (string.Empty, 0);
    }
}

static (string Host, int Port) GetUriEndpoint(
    string? value,
    int defaultPort,
    string? defaultScheme = null
)
{
    var endpoint = value?.Trim() ?? string.Empty;
    if (!string.IsNullOrEmpty(defaultScheme) && !endpoint.Contains("://", StringComparison.Ordinal))
        endpoint = $"{defaultScheme}://{endpoint}";

    return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        ? (uri.Host, uri.IsDefaultPort ? defaultPort : uri.Port)
        : (string.Empty, 0);
}

static (string Host, int Port) GetRedisEndpoint(string? connectionString)
{
    try
    {
        var endpoint = ConfigurationOptions
            .Parse(connectionString ?? string.Empty)
            .EndPoints.FirstOrDefault();
        return endpoint switch
        {
            DnsEndPoint dns => (dns.Host, dns.Port),
            IPEndPoint ip => (ip.Address.ToString(), ip.Port),
            _ => (string.Empty, 0),
        };
    }
    catch (ArgumentException)
    {
        return (string.Empty, 0);
    }
}

public partial class Program;
