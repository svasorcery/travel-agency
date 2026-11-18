using System.Buffers.Binary;
using System.Data.Common;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Persistence;

public sealed class TripFlightLinkEntity
{
    public Guid TripId { get; set; }
    public string OwnerIssuer { get; set; } = "";
    public Guid OwnerSubject { get; set; }
    public long LinksRevision { get; set; }
    public string ProtectedPayload { get; set; } = "";
}

public sealed class TripFlightLinkStore(TripsDbContext db) : ITripFlightLinkStore
{
    private DbSet<TripFlightLinkEntity> Rows => db.Set<TripFlightLinkEntity>();

    public Task<ErrorOr<TripFlightLinkRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    ) =>
        Guard<TripFlightLinkRecord?>(async () =>
        {
            // The left join distinguishes missing parent, untouched collection, and damaged child in a single snapshot.
            var row = await (
                from parent in db.Plans.AsNoTracking()
                where
                    parent.TripId == tripId
                    && parent.OwnerIssuer == owner.Issuer
                    && parent.OwnerSubject == owner.Subject
                join link in Rows.AsNoTracking() on parent.TripId equals link.TripId into matches
                from link in matches.DefaultIfEmpty()
                select new
                {
                    Id = (Guid?)link.TripId,
                    Revision = (long?)link.LinksRevision,
                    Issuer = link.OwnerIssuer,
                    Subject = (Guid?)link.OwnerSubject,
                    Envelope = link.ProtectedPayload.Length <= ITripFlightLinkProtector.MaximumBytes
                        ? link.ProtectedPayload
                        : null,
                }
            ).SingleOrDefaultAsync(ct);
            if (row is null)
                return TripErrors.NotFound;
            if (row.Id is null)
                return (TripFlightLinkRecord?)null;
            if (
                row.Id != tripId
                || row.Revision is null or <= 0
                || row.Issuer != owner.Issuer
                || row.Subject != owner.Subject
                || string.IsNullOrEmpty(row.Envelope)
            )
                return TripErrors.Unavailable;
            return new TripFlightLinkRecord(new(owner, tripId, row.Revision.Value), row.Envelope);
        });

    public Task<ErrorOr<Success>> CommitAsync(
        TripFlightLinkBinding expected,
        TripFlightLinkRecord? replacement,
        CancellationToken ct
    ) =>
        Guard<Success>(async () =>
        {
            if (
                expected.TripId == Guid.Empty
                || expected.Revision < 0
                || replacement is not null
                    && (
                        expected.Revision == long.MaxValue
                        || replacement.Binding
                            != (expected with { Revision = expected.Revision + 1 })
                        || string.IsNullOrEmpty(replacement.ProtectedPayload)
                        || replacement.ProtectedPayload.Length
                            > ITripFlightLinkProtector.MaximumBytes
                    )
            )
                return TripErrors.InvalidInput;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var parent = await db
                .Database.SqlQuery<Guid>(
                    $"""
                    SELECT trip_id AS "Value" FROM trips.trip_plans WHERE trip_id = {expected.TripId}
                    AND owner_issuer = {expected.Owner.Issuer} AND owner_subject = {expected.Owner.Subject}
                    FOR KEY SHARE
                    """
                )
                .SingleOrDefaultAsync(ct);
            if (parent == Guid.Empty)
                return TripErrors.NotFound;

            // A virtual revision zero has no child row to lock. Serialize link writers for
            // this trip, including no-ops, without blocking an ordinary calendar update.
            var key = BinaryPrimitives.ReadInt32BigEndian(expected.TripId.ToByteArray());
            const int flightLinkLockNamespace = 0x54464c31;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({flightLinkLockNamespace}, {key})",
                ct
            );
            var current = await Rows.AsNoTracking()
                .Where(x => x.TripId == expected.TripId)
                .Select(x => new
                {
                    x.LinksRevision,
                    x.OwnerIssuer,
                    x.OwnerSubject,
                })
                .SingleOrDefaultAsync(ct);
            if (
                current is not null
                && (
                    current.OwnerIssuer != expected.Owner.Issuer
                    || current.OwnerSubject != expected.Owner.Subject
                    || current.LinksRevision <= 0
                )
            )
                return TripErrors.Unavailable;
            if ((current?.LinksRevision ?? 0) != expected.Revision)
                return TripErrors.RevisionMismatch;
            if (replacement is not null)
            {
                if (current is null)
                {
                    var entity = new TripFlightLinkEntity
                    {
                        TripId = expected.TripId,
                        OwnerIssuer = expected.Owner.Issuer,
                        OwnerSubject = expected.Owner.Subject,
                        LinksRevision = replacement.Binding.Revision,
                        ProtectedPayload = replacement.ProtectedPayload,
                    };
                    Rows.Add(entity);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                    }
                    finally
                    {
                        db.Entry(entity).State = EntityState.Detached;
                    }
                }
                else
                {
                    var changed = await Rows.Where(x =>
                            x.TripId == expected.TripId
                            && x.OwnerIssuer == expected.Owner.Issuer
                            && x.OwnerSubject == expected.Owner.Subject
                            && x.LinksRevision == expected.Revision
                        )
                        .ExecuteUpdateAsync(
                            set =>
                                set.SetProperty(x => x.LinksRevision, replacement.Binding.Revision)
                                    .SetProperty(
                                        x => x.ProtectedPayload,
                                        replacement.ProtectedPayload
                                    ),
                            ct
                        );
                    if (changed != 1)
                        return TripErrors.RevisionMismatch;
                }
            }
            await transaction.CommitAsync(ct);
            return Result.Success;
        });

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
                        or TimeoutException
            )
        {
            return TripErrors.Unavailable;
        }
    }
}
