using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

/// <summary>
/// Healthcheck that pings the Duffel <c>/api/identity</c> endpoint (per spec §19).
/// A 2xx response → <see cref="HealthStatus.Healthy"/>; any failure → <see cref="HealthStatus.Unhealthy"/>.
/// <para>
/// Uses the probe-only named client <c>"duffel-health"</c> which bypasses the production
/// resilience pipeline (retries + circuit breaker). This prevents the healthcheck from
/// hanging when the circuit breaker is open and ensures probe traffic cannot interfere
/// with the breaker's <c>MinimumThroughput</c> accounting.
/// </para>
/// </summary>
public sealed class DuffelHealthCheck(IHttpClientFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var client = factory.CreateClient("duffel-health");
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
