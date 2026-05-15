using Microsoft.Extensions.Diagnostics.HealthChecks;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

/// <summary>
/// Healthcheck that pings the Duffel <c>/api/identity</c> endpoint (per spec §19).
/// A 2xx response → <see cref="HealthStatus.Healthy"/>; any failure → <see cref="HealthStatus.Unhealthy"/>.
/// </summary>
public sealed class DuffelHealthCheck(DuffelClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var response = await client.GetAsync("/api/identity", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Duffel API is reachable.")
                : HealthCheckResult.Unhealthy($"Duffel API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Duffel API ping failed.", ex);
        }
    }
}
