using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector
var postgres = builder
    .AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume();

if (builder.Environment.IsDevelopment())
{
    postgres.WithPgAdmin();
}

var travelDb = postgres.AddDatabase("travel");

// Redis
var redis = builder.AddRedis("redis").WithDataVolume();

// NATS JetStream
var nats = builder.AddNats("nats").WithJetStream().WithDataVolume();

// Keycloak
var keycloak = builder.AddKeycloak("keycloak", port: 8180).WithDataVolume();

if (builder.Environment.IsDevelopment())
{
    // Import dev realm (contains dev@travel.local / dev123 test user — not for production).
    keycloak.WithRealmImport("../../infra/keycloak");
}

// Mailpit (dev-only SMTP catcher). In production, configure Smtp__Host via environment variable.
IResourceBuilder<ContainerResource>? mailpit = null;
if (builder.Environment.IsDevelopment())
{
    mailpit = builder
        .AddContainer("mailpit", "axllent/mailpit", "v1.20")
        .WithEndpoint(port: 8025, targetPort: 8025, name: "ui")
        .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp");
}

var host = builder
    .AddProject<Projects.Travel_Host>("host")
    .WithEndpoint("http", e => e.Port = 5099, createIfNotExists: false)
    .WithReference(travelDb)
    .WithReference(redis)
    .WithReference(nats)
    .WithReference(keycloak);

if (mailpit is not null)
{
    host.WithEnvironment("Smtp__Host", mailpit.GetEndpoint("smtp"));
}

var ai = builder
    .AddProject<Projects.Travel_AI>("ai")
    .WithReference(travelDb)
    .WithReference(redis)
    .WithReference(nats);

// Optional observability stack (gated by env flag)
if (builder.Configuration.GetValue<bool>("ENABLE_OBSERVABILITY_STACK"))
{
    var loki = builder
        .AddContainer("loki", "grafana/loki", "3.2.0")
        .WithEndpoint(port: 3100, targetPort: 3100);

    var tempo = builder
        .AddContainer("tempo", "grafana/tempo", "2.6.0")
        .WithEndpoint(port: 3200, targetPort: 3200);

    builder
        .AddContainer("grafana", "grafana/grafana", "11.3.0")
        .WithEndpoint(port: 3000, targetPort: 3000);
}

await builder.Build().RunAsync();
