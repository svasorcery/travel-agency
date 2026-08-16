using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Travel.Shared.Infrastructure.Initialization;

/// <summary>
/// Reports readiness only after initialization completes successfully.
/// </summary>
public sealed class InitializationHealthCheck(AppInitializer initializer) : IHealthCheck
{
    public const string Name = "initialization";
    public const string ReadinessTag = "ready";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default
    ) => Task.FromResult(CreateResult(initializer.State));

    private static HealthCheckResult CreateResult(InitializationState state) =>
        state == InitializationState.Succeeded
            ? HealthCheckResult.Healthy("Initialization completed.")
            : HealthCheckResult.Unhealthy($"Initialization state: {state}.");
}
