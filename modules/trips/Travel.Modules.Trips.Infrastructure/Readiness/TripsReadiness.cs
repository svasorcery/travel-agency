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
            if (
                (await db.Database.GetPendingMigrationsAsync(ct)).Any(id =>
                    id != TripHotelLinkStore.AddTripHotelLinksMigrationId
                )
            )
                return false;
            await db.Database.ExecuteSqlRawAsync(
                "SELECT trip_id, owner_issuer, owner_subject, revision, created_at, updated_at, protected_payload FROM trips.trip_plans WHERE FALSE",
                ct
            );
            await db.Database.ExecuteSqlRawAsync(
                "SELECT trip_id, owner_issuer, owner_subject, links_revision, protected_payload FROM trips.trip_flight_links WHERE FALSE",
                ct
            );
            // The saved option is part of a plan's history even when issuance/writes are off.
            // Verify the nullable column and the actual receipt key/index columns.
            return await db
                .Database.SqlQuery<bool>(
                    $"""
                    SELECT
                        EXISTS (SELECT FROM pg_catalog.pg_attribute
                            WHERE attrelid = to_regclass('trips.trip_plans') AND attname = 'protected_option'
                            AND NOT attisdropped AND NOT attnotnull AND atttypid = 'text'::regtype)
                        AND (SELECT count(*) = 4 FROM pg_catalog.pg_attribute
                            WHERE attrelid = to_regclass('trips.saved_option_receipts') AND attnotnull AND NOT attisdropped
                            AND (attname::text, format_type(atttypid, atttypmod)) IN (
                                ('trip_id', 'uuid'), ('owner_issuer', 'character varying(512)'),
                                ('owner_subject', 'uuid'), ('expires_at', 'timestamp with time zone')))
                        AND EXISTS (SELECT FROM pg_catalog.pg_index i JOIN pg_catalog.pg_attribute a
                            ON a.attrelid = i.indrelid AND a.attnum = i.indkey[0]
                            WHERE i.indrelid = to_regclass('trips.saved_option_receipts') AND a.attname = 'trip_id'
                                AND i.indisprimary AND i.indisvalid AND i.indnkeyatts = 1)
                        AND EXISTS (SELECT FROM pg_catalog.pg_index i JOIN pg_catalog.pg_attribute a
                            ON a.attrelid = i.indrelid AND a.attnum = i.indkey[0]
                            WHERE i.indrelid = to_regclass('trips.saved_option_receipts') AND a.attname = 'expires_at'
                                AND i.indisvalid AND i.indisready AND i.indpred IS NULL AND i.indnkeyatts = 1)
                        AS "Value"
                    """
                )
                .SingleAsync(ct);
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
