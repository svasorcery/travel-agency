using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.HealthChecks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.HealthChecks;

[Trait("Category", "Integration")]
public sealed class FlightsHealthCheckTests : IDisposable
{
    private readonly WireMockServer _server;

    public FlightsHealthCheckTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose() => _server.Stop();

    // ── DuffelHealthCheck ────────────────────────────────────────────────────────

    [Fact]
    public async Task Duffel_healthcheck_reports_healthy_on_ping_ok()
    {
        // Arrange — Duffel /api/identity returns 200
        _server
            .Given(Request.Create().WithPath("/api/identity").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var factory = BuildFactory("duffel-health", _server.Url!);
        var check = new DuffelHealthCheck(factory);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Duffel_healthcheck_reports_unhealthy_on_ping_failure()
    {
        // Arrange — Duffel /api/identity returns 500
        _server
            .Given(Request.Create().WithPath("/api/identity").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("error"));

        var factory = BuildFactory("duffel-health", _server.Url!);
        var check = new DuffelHealthCheck(factory);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    // ── TravelpayoutsHealthCheck ──────────────────────────────────────────────────

    [Fact]
    public async Task Travelpayouts_healthcheck_reports_healthy_on_ping_ok()
    {
        // Arrange — Travelpayouts prices_for_dates returns 200
        _server
            .Given(Request.Create().WithPath("/v3/prices_for_dates").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var factory = BuildFactory("travelpayouts-health", _server.Url!);
        var check = new TravelpayoutsHealthCheck(factory);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Travelpayouts_healthcheck_reports_unhealthy_on_ping_failure()
    {
        // Arrange — Travelpayouts endpoint returns 503
        _server
            .Given(Request.Create().WithPath("/v3/prices_for_dates").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503).WithBody("error"));

        var factory = BuildFactory("travelpayouts-health", _server.Url!);
        var check = new TravelpayoutsHealthCheck(factory);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="IHttpClientFactory"/> that returns a bare <see cref="HttpClient"/>
    /// pointed at <paramref name="baseUrl"/> for the given named client. No resilience handlers
    /// are registered — this mirrors the probe-only client intent in production registration.
    /// </summary>
    private static IHttpClientFactory BuildFactory(string clientName, string baseUrl)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(clientName, c => c.BaseAddress = new Uri(baseUrl));
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }
}
