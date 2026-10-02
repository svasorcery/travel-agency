using Microsoft.Extensions.Diagnostics.HealthChecks;
using Travel.Modules.Flights.Infrastructure.Privacy;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

public sealed class FlightsPiiProtectionHealthCheck(FlightsPiiProtectionProvider provider)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = provider.Availability;
        return Task.FromResult(
            state == PiiProtectionAvailability.Ready
                ? HealthCheckResult.Healthy(
                    "New PII protection available; historical payloads not audited."
                )
                : HealthCheckResult.Degraded(
                    $"PII protection {state}; protected writes unavailable."
                )
        );
    }
}
