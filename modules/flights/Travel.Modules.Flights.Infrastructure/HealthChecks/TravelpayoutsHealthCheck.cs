using Microsoft.Extensions.Diagnostics.HealthChecks;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

/// <summary>
/// Healthcheck that pings the Travelpayouts <c>prices_for_dates</c> endpoint with
/// <c>test=1</c> so no quota is consumed.
/// A 2xx response → <see cref="HealthStatus.Healthy"/>; any failure → <see cref="HealthStatus.Unhealthy"/>.
/// </summary>
public sealed class TravelpayoutsHealthCheck(TravelpayoutsClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
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
