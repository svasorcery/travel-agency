using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Travel.Modules.Flights.Infrastructure.Persistence;

namespace Travel.Modules.Flights.Infrastructure.HealthChecks;

/// <summary>Sentinel gate only. Full historical correctness requires per-stream Validate.</summary>
public sealed class BookingProjectionBootstrapHealthCheck(FlightsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var sentinel = await db
                .Orders.AsNoTracking()
                .AnyAsync(x => x.ProjectedStreamVersion < 0, cancellationToken);
            return sentinel
                ? HealthCheckResult.Unhealthy("Booking projection bootstrap sentinel remains.")
                : HealthCheckResult.Healthy(
                    "No bootstrap sentinels; per-stream validation is still required."
                );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Booking projection bootstrap storage unavailable.");
        }
    }
}
