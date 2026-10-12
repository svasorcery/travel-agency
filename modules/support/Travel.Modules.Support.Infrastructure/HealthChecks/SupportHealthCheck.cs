using Microsoft.Extensions.Diagnostics.HealthChecks;
using Travel.Modules.Support.Infrastructure.Persistence.Initialization;
using Travel.Modules.Support.Infrastructure.Privacy;
using Travel.Modules.Support.Infrastructure.Retention;

namespace Travel.Modules.Support.Infrastructure.HealthChecks;

public sealed class SupportHealthCheck(
    SupportSchemaReadiness schema,
    SupportTextProtectionProvider protection,
    SupportRetentionStatus retention
) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            schema.IsReady
            && protection.Availability == SupportProtectionAvailability.Ready
            && !retention.LastPassFailed
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Support is unavailable.")
        );
}
