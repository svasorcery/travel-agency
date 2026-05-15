using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.HealthChecks;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
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

        var client = BuildDuffelClient();
        var check = new DuffelHealthCheck(client);

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

        var client = BuildDuffelClient();
        var check = new DuffelHealthCheck(client);

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

        var client = BuildTravelpayoutsClient();
        var check = new TravelpayoutsHealthCheck(client);

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

        var client = BuildTravelpayoutsClient();
        var check = new TravelpayoutsHealthCheck(client);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private DuffelClient BuildDuffelClient()
    {
        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = _server.Url!,
                ApiVersion = "v2",
                ApiKey = "test_key",
            }
        );
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        return new DuffelClient(http, opts);
    }

    private TravelpayoutsClient BuildTravelpayoutsClient()
    {
        var opts = Options.Create(
            new TravelpayoutsOptions
            {
                BaseUrl = _server.Url!,
                ApiVersion = "v3",
                ApiToken = "test_token",
                PartnerMarker = "test_marker",
            }
        );
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        return new TravelpayoutsClient(http, opts);
    }
}
