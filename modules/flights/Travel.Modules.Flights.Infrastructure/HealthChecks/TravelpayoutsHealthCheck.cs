using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

/// <summary>
/// Healthcheck that pings the Travelpayouts <c>prices_for_dates</c> endpoint with
/// <c>test=1</c> so no quota is consumed.
/// A 2xx response → <see cref="HealthStatus.Healthy"/>; any failure → <see cref="HealthStatus.Unhealthy"/>.
/// <para>
/// Uses the probe-only named client <c>"travelpayouts-health"</c> which bypasses the
/// production resilience pipeline (retries + circuit breaker). This prevents the healthcheck
/// from hanging when the circuit breaker is open and ensures probe traffic cannot interfere
/// with the breaker's <c>MinimumThroughput</c> accounting.
/// </para>
/// </summary>
public sealed class TravelpayoutsHealthCheck(IHttpClientFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var client = factory.CreateClient("travelpayouts-health");
            var response = await client.GetAsync("/v3/prices_for_dates?test=1", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Travelpayouts API is reachable.")
                : HealthCheckResult.Unhealthy(
                    $"Travelpayouts API returned {(int)response.StatusCode}."
                );
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Travelpayouts API ping failed.", ex);
        }
    }
}
