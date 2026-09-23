using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Runtime;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

/// <summary>Checks that persisted delivery diagnostics can be read; it does not validate projection freshness.</summary>
public sealed class BookingProjectionHealthCheck(IWolverineRuntime runtime) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var counts = await runtime.Storage.Admin.FetchCountsAsync();
            var deadLetters = await runtime.Storage.DeadLetters.QueryAsync(
                new DeadLetterEnvelopeQuery { PageNumber = 1, PageSize = 1 },
                cancellationToken
            );
            var description =
                $"Persisted incoming {counts.Incoming}, scheduled {counts.Scheduled}, outgoing {counts.Outgoing}, dead letters {deadLetters.TotalCount}.";
            return deadLetters.TotalCount == 0
                ? HealthCheckResult.Healthy($"No dead letters. {description}")
                : HealthCheckResult.Degraded(description);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Booking delivery diagnostics unavailable.");
        }
    }
}
