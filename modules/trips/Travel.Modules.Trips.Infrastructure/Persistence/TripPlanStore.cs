using System.Buffers.Binary;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Persistence;

public sealed class TripPlanStore(TripsDbContext db) : ITripPlanStore
{
    public Task<ErrorOr<TripPlanRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    ) =>
        Guard<TripPlanRecord?>(async () =>
            (
                await BoundedPayload(
                        db.Plans.AsNoTracking()
                            .Where(x =>
                                x.TripId == tripId
                                && x.OwnerIssuer == owner.Issuer
                                && x.OwnerSubject == owner.Subject
                            )
                    )
                    .SingleOrDefaultAsync(ct)
            )?.ToRecord()
        );

    public Task<ErrorOr<TripPlanRecordPage>> ListAsync(
        OwnerIdentity owner,
        TripPlanPosition? after,
        int limit,
        CancellationToken ct
    ) =>
        Guard<TripPlanRecordPage>(async () =>
        {
            if (limit is < 1 or > 20)
                return TripErrors.InvalidInput;
            var rows = db
                .Plans.AsNoTracking()
                .Where(x => x.OwnerIssuer == owner.Issuer && x.OwnerSubject == owner.Subject);
            if (after is not null)
                rows = rows.Where(x =>
                    x.UpdatedAt < after.UpdatedAt
                    || (x.UpdatedAt == after.UpdatedAt && x.TripId.CompareTo(after.TripId) > 0)
                );
            var page = await BoundedPayload(
                    rows.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.TripId).Take(limit + 1)
                )
                .ToArrayAsync(ct);
            return new TripPlanRecordPage(
                page.Take(limit).Select(x => x.ToRecord()).ToArray(),
                page.Length > limit
            );
        });

    public Task<ErrorOr<Success>> TryCreateAsync(
        TripPlanRecord record,
        int ownerCapacity,
        CancellationToken ct
    ) =>
        Write(async () =>
        {
            if (ownerCapacity is < 1 or > 100)
                return TripErrors.InvalidInput;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Transaction-scoped PostgreSQL lock serializes the final owner slot across processes.
            var hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    record.Owner.Issuer + "\n" + record.Owner.Subject.ToString("N")
                )
            );
            var key = BinaryPrimitives.ReadInt64BigEndian(hash);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({key})",
                ct
            );
            if (
                await db.Plans.CountAsync(
                    x =>
                        x.OwnerIssuer == record.Owner.Issuer
                        && x.OwnerSubject == record.Owner.Subject,
                    ct
                ) >= ownerCapacity
            )
                return TripErrors.CapacityExceeded;
            db.Plans.Add(TripPlanEntity.From(record));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Success;
        });

    public Task<ErrorOr<Success>> TryReplaceAsync(
        long expectedRevision,
        TripPlanRecord record,
        CancellationToken ct
    ) =>
        Guard<Success>(async () =>
        {
            var changed = await db
                .Plans.Where(x =>
                    x.TripId == record.TripId
                    && x.OwnerIssuer == record.Owner.Issuer
                    && x.OwnerSubject == record.Owner.Subject
                    && x.Revision == expectedRevision
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.Revision, record.Revision)
                            .SetProperty(x => x.UpdatedAt, record.UpdatedAt)
                            .SetProperty(x => x.ProtectedPayload, record.ProtectedPayload),
                    ct
                );
            return changed == 1
                ? Result.Success
                : await MissingOrStale(record.Owner, record.TripId, ct);
        });

    public Task<ErrorOr<Success>> TryDeleteAsync(
        OwnerIdentity owner,
        Guid tripId,
        long expectedRevision,
        CancellationToken ct
    ) =>
        Guard<Success>(async () =>
        {
            var changed = await db
                .Plans.Where(x =>
                    x.TripId == tripId
                    && x.OwnerIssuer == owner.Issuer
                    && x.OwnerSubject == owner.Subject
                    && x.Revision == expectedRevision
                )
                .ExecuteDeleteAsync(ct);
            return changed == 1 ? Result.Success : await MissingOrStale(owner, tripId, ct);
        });

    // Project a failure sentinel in SQL for corrupt oversized envelopes: never materialize an unbounded DB value.
    // A sentinel is not returned to clients; Application fails the entire detail/page with Unavailable.
    private static IQueryable<TripPlanEntity> BoundedPayload(IQueryable<TripPlanEntity> rows) =>
        rows.Select(x => new TripPlanEntity
        {
            TripId = x.TripId,
            OwnerIssuer = x.OwnerIssuer,
            OwnerSubject = x.OwnerSubject,
            Revision = x.Revision,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
            ProtectedPayload =
                x.ProtectedPayload.Length > 200000 ? string.Empty : x.ProtectedPayload,
        });

    private async Task<ErrorOr<Success>> MissingOrStale(
        OwnerIdentity owner,
        Guid id,
        CancellationToken ct
    ) =>
        await db
            .Plans.AsNoTracking()
            .AnyAsync(
                x =>
                    x.TripId == id
                    && x.OwnerIssuer == owner.Issuer
                    && x.OwnerSubject == owner.Subject,
                ct
            )
            ? TripErrors.RevisionMismatch
            : TripErrors.NotFound;

    private async Task<ErrorOr<Success>> Write(Func<Task<ErrorOr<Success>>> operation)
    {
        try
        {
            return await Guard(operation);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<ErrorOr<T>> Guard<T>(Func<Task<ErrorOr<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
            when (ex
                    is DbException
                        or DbUpdateException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return TripErrors.Unavailable;
        }
    }
}
