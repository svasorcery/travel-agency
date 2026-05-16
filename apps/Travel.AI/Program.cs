using Anthropic;
using Anthropic.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Wolverine;
using Wolverine.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

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
        opts.UseSnakeCaseNamingConvention();
    }
);

// Register the Anthropic-backed IChatClient.
// API key is read from Anthropic:ApiKey (user secrets / environment).
// If the key is absent the client still registers; calls will fail at invocation time
// which is the accepted graceful-degradation behaviour for BYO-key configurations.
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"] ?? string.Empty;
builder.Services.AddSingleton<IChatClient>(_ =>
    new AnthropicClient(new ClientOptions { ApiKey = anthropicApiKey }).AsIChatClient(
        "claude-opus-4-7"
    )
);

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Wolverine: discover handlers in this assembly and wire NATS transport.
// The NATS connection string is injected by Aspire as "ConnectionStrings:nats".
var natsUrl = builder.Configuration.GetConnectionString("nats") ?? "nats://localhost:4222";
builder.Host.UseWolverine(opts =>
{
    // Wire NATS as the transport so Travel.Host can send NlSearchRequested here
    // via Wolverine request/reply and receive NlSearchParsed back.
    opts.UseNats(natsUrl);

    // Listen to the NL-search subject published by Travel.Host
    opts.ListenToNatsSubject("travel.ai.nl_search");
});

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/", () => "Travel.AI service running");

await app.RunAsync();
