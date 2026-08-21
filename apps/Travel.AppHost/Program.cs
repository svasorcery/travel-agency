using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var useVolumes = builder.Configuration.GetValue("UseVolumes", true);

// PostgreSQL with pgvector
var postgres = builder.AddPostgres("postgres").WithImage("pgvector/pgvector", "pg17");

if (useVolumes)
{
    postgres.WithDataVolume();
}

if (builder.Environment.IsDevelopment())
{
    postgres.WithPgAdmin();
}

var travelDb = postgres.AddDatabase("travel");

// Redis
var redis = builder.AddRedis("redis");
if (useVolumes)
{
    redis.WithDataVolume();
}

// NATS JetStream
var nats = builder.AddNats("nats").WithJetStream();
if (useVolumes)
{
    nats.WithDataVolume();
}

// Keycloak
var keycloak = builder.AddKeycloak("keycloak", port: 8180);
if (useVolumes)
{
    keycloak.WithDataVolume();
}

if (builder.Environment.IsDevelopment())
{
    // Import dev realm (contains dev@travel.local / dev123 test user — not for production).
    keycloak.WithRealmImport("../../infra/keycloak");
}

// Mailpit (dev-only SMTP catcher). In production, configure Flights__Smtp__Host explicitly.
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
    .WithEndpoint(
        targetPort: 5098,
        port: 5098,
        scheme: "http",
        name: "health-internal",
        env: "HealthEndpoints__InternalPort",
        isExternal: false,
        isProxied: false
    )
    .WithHttpHealthCheck("/health/ready", endpointName: "health-internal")
    .WithReference(travelDb)
    .WithReference(redis)
    .WithReference(nats)
    .WithReference(keycloak)
    .WaitFor(travelDb)
    .WaitFor(nats)
    .WaitFor(redis)
    .WaitFor(keycloak);

if (mailpit is not null)
{
    var smtpEndpoint = mailpit.GetEndpoint("smtp");
    host.WithEnvironment("Flights__Smtp__Host", smtpEndpoint.Property(EndpointProperty.Host));
    host.WithEnvironment("Flights__Smtp__Port", smtpEndpoint.Property(EndpointProperty.Port));
}

var ai = builder
    .AddProject<Projects.Travel_AI>("ai")
    .WithEndpoint(
        targetPort: 5159,
        port: 5159,
        scheme: "http",
        name: "health-internal",
        env: "HealthEndpoints__InternalPort",
        isExternal: false,
        isProxied: false
    )
    .WithHttpHealthCheck("/health/ready", endpointName: "health-internal")
    .WithReference(travelDb)
    .WithReference(nats)
    .WaitFor(travelDb)
    .WaitFor(nats);

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
