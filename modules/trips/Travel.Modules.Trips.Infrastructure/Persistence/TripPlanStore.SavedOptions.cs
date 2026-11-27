using System.Data;
using System.Text;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Persistence;

public sealed partial class TripPlanStore
{
    public Task<ErrorOr<SavedOptionWrite>> TrySaveAsync(
        OwnerIdentity owner,
        Guid tripId,
        DateTimeOffset expiresAt,
        Func<CancellationToken, Task<ErrorOr<NewSavedOption>>> prepareFirstWrite,
        CancellationToken ct
    ) =>
        Write<SavedOptionWrite>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                ct
            );
            await LockOwnerAsync(owner, ct);
            var existing = await db
                .Plans.Where(x =>
                    x.TripId == tripId
                    && x.OwnerIssuer == owner.Issuer
                    && x.OwnerSubject == owner.Subject
                )
                .Select(x => new { HasOption = x.ProtectedOption != null })
                .SingleOrDefaultAsync(ct);
            if (existing is not null)
                return existing.HasOption
                    ? new SavedOptionWrite(tripId, false)
                    : TripErrors.SelectionUnavailable;
            if (
                await db.SavedOptionReceipts.AnyAsync(
                    x =>
                        x.TripId == tripId
                        && x.OwnerIssuer == owner.Issuer
                        && x.OwnerSubject == owner.Subject,
                    ct
                )
            )
                return TripErrors.SelectionUnavailable;
            if (!await WithinFirstUseAsync(expiresAt, ct))
                return TripErrors.SelectionUnavailable;
            if (await AtCapacityAsync(owner, TripPlanService.OwnerCapacity, ct))
                return TripErrors.CapacityExceeded;
            var prepared = await prepareFirstWrite(ct);
            if (prepared.IsError)
                return prepared.Errors;
            var (record, protectedOption) = prepared.Value;
            if (
                record.Owner != owner
                || record.TripId != tripId
                || record.Revision != 1
                || string.IsNullOrEmpty(protectedOption)
                || Encoding.UTF8.GetByteCount(protectedOption) > NewSavedOption.MaximumEnvelopeBytes
            )
                return TripErrors.Unavailable;

            var plan = TripPlanEntity.From(record);
            plan.ProtectedOption = protectedOption;
            db.Plans.Add(plan);
            db.SavedOptionReceipts.Add(
                new()
                {
                    TripId = tripId,
                    OwnerIssuer = owner.Issuer,
                    OwnerSubject = owner.Subject,
                    ExpiresAt = expiresAt,
                }
            );
            await PruneExpiredReceiptsAsync(ct);
            if (!await WithinFirstUseAsync(expiresAt, ct))
                return TripErrors.SelectionUnavailable;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new SavedOptionWrite(tripId, true);
        });

    private Task<bool> WithinFirstUseAsync(DateTimeOffset expiresAt, CancellationToken ct) =>
        db
            .Database.SqlQuery<bool>($"SELECT clock_timestamp() < {expiresAt} AS \"Value\"")
            .SingleAsync(ct);

    // SKIP LOCKED keeps one owner's housekeeping from waiting on another admission.
    public Task<int> PruneExpiredReceiptsAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            """
            WITH boundary AS MATERIALIZED (SELECT clock_timestamp() AS cutoff), expired AS (
                SELECT trip_id FROM trips.saved_option_receipts
                WHERE expires_at <= (SELECT cutoff FROM boundary)
                ORDER BY expires_at LIMIT 100 FOR UPDATE SKIP LOCKED
            )
            DELETE FROM trips.saved_option_receipts r USING expired WHERE r.trip_id = expired.trip_id
            """,
            ct
        );

    Task<ErrorOr<SavedOptionEnvelope>> ISavedOptionStore.LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    ) =>
        Guard<SavedOptionEnvelope>(async () =>
        {
            var values = await db
                .Database.SqlQuery<string?>(
                    $"""
                    SELECT CASE WHEN octet_length(protected_option) > {NewSavedOption.MaximumEnvelopeBytes}
                                THEN '' ELSE protected_option END AS "Value"
                    FROM trips.trip_plans
                    WHERE trip_id = {tripId} AND owner_issuer = {owner.Issuer} AND owner_subject = {owner.Subject}
                    """
                )
                .ToArrayAsync(ct);
            if (values.Length == 0)
                return TripErrors.NotFound;
            return values[0] == "" ? TripErrors.Unavailable : new SavedOptionEnvelope(values[0]);
        });
}
