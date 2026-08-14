using Anthropic;
using Anthropic.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Npgsql;
using Travel.AI.Configuration;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Initialization;
using Travel.ServiceDefaults.Health;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInternalHealthEndpoints(5159);
builder.Services.AddTravelAiOptions(builder.Configuration, builder.Environment);

// OTel meter for gen_ai.* instruments so the Aspire/OTLP exporter picks it up.
builder.Services.AddSingleton<AiMetrics>();
builder
    .Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(AiMetrics.MeterName))
    .WithTracing(t =>
        t
        // Travel.AI gen_ai.completion spans.
        .AddSource(Travel.AI.Observability.AiActivitySource.Name)
            // Register the Flights activity source so saga-transition spans from Travel.Flights
            // are collected when distributed traces propagate through NATS.
            .AddSource("Travel.Flights")
    );

builder.AddNpgsqlDbContext<AiDbContext>(
    "travel",
    configureDbContextOptions: opts =>
    {
        opts.UseNpgsql(AiDbContextConfiguration.ConfigureNpgsql).UseSnakeCaseNamingConvention();
    }
);

var postgresHealthEndpoint = GetPostgresEndpoint(
    builder.Configuration.GetConnectionString("travel")
);
var natsHealthEndpoint = GetNatsEndpoint(builder.Configuration.GetConnectionString("nats"));
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

// Register the Anthropic-backed IChatClient.
// API key is read from Anthropic:ApiKey (user secrets / environment).
// Production startup validation requires the key; Development may omit it until an
// Anthropic-backed request is exercised.
builder.Services.AddSingleton<IChatClient>(sp =>
    new AnthropicClient(
        new ClientOptions
        {
            ApiKey = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.ApiKey,
        }
    ).AsIChatClient("claude-opus-4-7")
);

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddInitializer<AiEfInitializer>();
builder.Services.AddAppInitialization();

// Wolverine: discover handlers in this assembly and wire NATS transport.
// The NATS connection string is injected by Aspire as "ConnectionStrings:nats".
var natsUrl = builder.Configuration.GetConnectionString("nats") ?? string.Empty;
builder.Host.UseWolverine(opts =>
{
    opts.ApplicationAssembly = typeof(Program).Assembly;

    // Wire NATS as the transport so Travel.Host can send NlSearchRequested here
    // via Wolverine request/reply and receive NlSearchParsed back.
    opts.UseNats(natsUrl);

    // Listen to the NL-search subject published by Travel.Host
    opts.ListenToNatsSubject("travel.ai.nl_search").UseQueueGroup("travel.ai.nl_search.workers");
});

var app = builder.Build();

// Validate owner connection options before Wolverine/Anthropic runtime services can consume
// missing or local Production settings. ValidateOnStart remains the host lifecycle gate.
_ = app.Services.GetRequiredService<IOptions<HealthEndpointOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<AiConnectionOptions>>().Value;
app.Services.GetRequiredService<IStartupValidator>().Validate();

app.MapDefaultEndpoints();
app.MapGet("/", () => "Travel.AI service running");

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

static (string Host, int Port) GetNatsEndpoint(string? value)
{
    return Uri.TryCreate(value, UriKind.Absolute, out var uri)
        ? (uri.Host, uri.IsDefaultPort ? 4222 : uri.Port)
        : (string.Empty, 0);
}

public partial class Program;
