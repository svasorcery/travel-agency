using System.Buffers.Binary;
using System.Data.Common;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Trips.Application.HotelLinks;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;

namespace Travel.Modules.Trips.Infrastructure.Persistence;

public sealed class TripHotelLinkEntity
{
    public Guid TripId { get; set; }
    public string OwnerIssuer { get; set; } = "";
    public Guid OwnerSubject { get; set; }
    public long Revision { get; set; }
    public string ProtectedPayload { get; set; } = "";
}

public sealed class TripHotelLinkStore(TripsDbContext db) : ITripHotelLinkStore
{
    internal const string AddTripHotelLinksMigrationId = "20261124121853_AddTripHotelLinks";

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try
        {
            if (
                !(await db.Database.GetAppliedMigrationsAsync(ct)).Contains(
                    AddTripHotelLinksMigrationId
                )
            )
                return false;

            await db.Database.ExecuteSqlRawAsync(
                "SELECT trip_id, owner_issuer, owner_subject, revision, protected_payload FROM trips.trip_hotel_links WHERE FALSE",
                ct
            );
            return await db
                .Database.SqlQuery<bool>(
                    $"""
                    SELECT count(*) = 5 AND (
                        SELECT count(*) = 3
                        FROM pg_catalog.pg_constraint
                        WHERE conrelid = to_regclass('trips.trip_hotel_links') AND convalidated
                            AND (
                                (conname = 'pk_trip_hotel_links' AND contype = 'p')
                                OR (conname = 'ck_trip_hotel_links_revision_positive' AND contype = 'c')
                                OR (conname = 'fk_trip_hotel_links_trip_plans_trip_id' AND contype = 'f'
                                    AND confrelid = to_regclass('trips.trip_plans') AND confdeltype = 'c')
                            )
                    ) AS "Value"
                    FROM pg_catalog.pg_attribute
                    WHERE attrelid = to_regclass('trips.trip_hotel_links') AND attnotnull AND NOT attisdropped
                        AND (attname::text, format_type(atttypid, atttypmod)) IN (
                            ('trip_id', 'uuid'), ('owner_issuer', 'character varying(512)'),
                            ('owner_subject', 'uuid'), ('revision', 'bigint'),
                            ('protected_payload', {$"character varying({TripHotelLinkProtector.MaximumEnvelopeLength})"})
                        )
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

    public Task<ErrorOr<TripHotelLinkRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    ) =>
        Guard<TripHotelLinkRecord?>(async () =>
        {
            var row = await (
                from parent in db.Plans.AsNoTracking()
                where
                    parent.TripId == tripId
                    && parent.OwnerIssuer == owner.Issuer
                    && parent.OwnerSubject == owner.Subject
                join link in db.HotelLinks.AsNoTracking()
                    on parent.TripId equals link.TripId
                    into matches
                from link in matches.DefaultIfEmpty()
                select new
                {
                    TripId = (Guid?)link.TripId,
                    Revision = (long?)link.Revision,
                    Issuer = link.OwnerIssuer,
                    Subject = (Guid?)link.OwnerSubject,
                    Envelope = link.ProtectedPayload.Length
                    <= TripHotelLinkProtector.MaximumEnvelopeLength
                        ? link.ProtectedPayload
                        : null,
                }
            ).SingleOrDefaultAsync(ct);

            if (row is null)
                return TripErrors.NotFound;
            if (row.TripId is null)
                return (TripHotelLinkRecord?)null;
            if (
                row.Revision is null or <= 0
                || row.Issuer != owner.Issuer
                || row.Subject != owner.Subject
                || string.IsNullOrEmpty(row.Envelope)
            )
                return TripErrors.Unavailable;

            return new TripHotelLinkRecord(new(owner, tripId, row.Revision.Value), row.Envelope);
        });

    public Task<ErrorOr<Success>> CommitAsync(
        TripHotelLinkBinding expected,
        TripHotelLinkRecord? replacement,
        CancellationToken ct
    ) =>
        Guard<Success>(async () =>
        {
            if (
                expected.Owner is null
                || expected.TripId == Guid.Empty
                || expected.Revision < 0
                || replacement is not null
                    && (
                        expected.Revision == long.MaxValue
                        || replacement.Binding
                            != (expected with { Revision = expected.Revision + 1 })
                        || string.IsNullOrEmpty(replacement.ProtectedPayload)
                        || replacement.ProtectedPayload.Length
                            > TripHotelLinkProtector.MaximumEnvelopeLength
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

            // Serialize mutations at virtual revision zero, while the parent lock coordinates deletion.
            const int hotelLinkLockNamespace = 0x54484c31;
            var lockKey = BinaryPrimitives.ReadInt32BigEndian(expected.TripId.ToByteArray());
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({hotelLinkLockNamespace}, {lockKey})",
                ct
            );
            var current = await db
                .HotelLinks.AsNoTracking()
                .Where(x => x.TripId == expected.TripId)
                .Select(x => new
                {
                    x.Revision,
                    x.OwnerIssuer,
                    x.OwnerSubject,
                })
                .SingleOrDefaultAsync(ct);
            if (
                current is not null
                && (
                    current.Revision <= 0
                    || current.OwnerIssuer != expected.Owner.Issuer
                    || current.OwnerSubject != expected.Owner.Subject
                )
            )
                return TripErrors.Unavailable;
            if ((current?.Revision ?? 0) != expected.Revision)
                return TripErrors.RevisionMismatch;

            if (replacement is not null)
            {
                var changed = current is null
                    ? await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO trips.trip_hotel_links (trip_id, owner_issuer, owner_subject, revision, protected_payload)
                        VALUES ({expected.TripId}, {expected.Owner.Issuer}, {expected.Owner.Subject}, {replacement.Binding.Revision}, {replacement.ProtectedPayload})
                        ON CONFLICT (trip_id) DO NOTHING
                        """,
                        ct
                    )
                    : await db
                        .HotelLinks.Where(x =>
                            x.TripId == expected.TripId
                            && x.OwnerIssuer == expected.Owner.Issuer
                            && x.OwnerSubject == expected.Owner.Subject
                            && x.Revision == expected.Revision
                        )
                        .ExecuteUpdateAsync(
                            setters =>
                                setters
                                    .SetProperty(x => x.Revision, replacement.Binding.Revision)
                                    .SetProperty(
                                        x => x.ProtectedPayload,
                                        replacement.ProtectedPayload
                                    ),
                            ct
                        );
                if (changed != 1)
                    return TripErrors.RevisionMismatch;
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
            when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            return TripErrors.Unavailable;
        }
    }
}
