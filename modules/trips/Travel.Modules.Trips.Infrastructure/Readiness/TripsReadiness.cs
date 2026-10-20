using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;

namespace Travel.Modules.Trips.Infrastructure.Readiness;

public sealed class TripsReadiness(IServiceScopeFactory scopes, TripsProtectionProvider protection)
    : ITripsReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        if (!protection.IsReady)
            return false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TripsDbContext>();
            if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
                return false;
            await db.Database.ExecuteSqlRawAsync(
                "SELECT trip_id, owner_issuer, owner_subject, revision, created_at, updated_at, protected_payload FROM trips.trip_plans WHERE FALSE",
                ct
            );
            return true;
        }
        catch (Exception ex)
            when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }
}

public sealed class TripsHealthCheck(ITripsReadiness readiness) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        await readiness.IsReadyAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Trips is unavailable.");
}
