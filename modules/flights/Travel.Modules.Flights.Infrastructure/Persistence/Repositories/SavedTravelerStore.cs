using System.Data.Common;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Repositories;

public sealed class SavedTravelerStore(FlightsDbContext db) : ISavedTravelerStore
{
    public Task<ErrorOr<SavedTravelerStoredRecord?>> GetOwnedAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    ) =>
        Guard<SavedTravelerStoredRecord?>(async () =>
        {
            var invalid = ValidateIdentity(ownerUserId, id);
            if (invalid is { } error)
                return error;
            var entity = await db
                .SavedTravelers.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId && x.Id == id.Value)
                .SingleOrDefaultAsync(ct);
            if (entity is null)
                return (SavedTravelerStoredRecord?)null;
            var record = ToRecord(entity);
            return record.IsError ? record.Errors : record.Value;
        });

    public Task<ErrorOr<IReadOnlyList<SavedTravelerStoredRecord>>> ListOwnedAsync(
        Guid ownerUserId,
        int offset,
        CancellationToken ct
    ) =>
        Guard<IReadOnlyList<SavedTravelerStoredRecord>>(async () =>
        {
            if (ownerUserId == Guid.Empty)
                return SavedTravelerErrors.IdentityInvalid;
            if (!SavedTravelerPaging.IsValid(offset))
                return SavedTravelerErrors.PageInvalid;
            var entities = await db
                .SavedTravelers.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Skip(offset)
                .Take(SavedTravelerPaging.ReadSize)
                .ToListAsync(ct);
            var records = new List<SavedTravelerStoredRecord>(entities.Count);
            foreach (var entity in entities)
            {
                var record = ToRecord(entity);
                if (record.IsError)
                    return record.Errors;
                records.Add(record.Value);
            }
            return ErrorOrFactory.From<IReadOnlyList<SavedTravelerStoredRecord>>(
                records.AsReadOnly()
            );
        });

    public Task<ErrorOr<SavedTravelerVisibility>> GetVisibilityAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    ) =>
        Guard<SavedTravelerVisibility>(async () =>
        {
            var invalid = ValidateIdentity(ownerUserId, id);
            if (invalid is { } error)
                return error;
            // Only owner metadata is projected; foreign ciphertext is never materialized.
            var owner = await db
                .SavedTravelers.AsNoTracking()
                .Where(x => x.Id == id.Value)
                .Select(x => (Guid?)x.OwnerUserId)
                .SingleOrDefaultAsync(ct);
            return owner is null ? SavedTravelerVisibility.Missing
                : owner == ownerUserId ? SavedTravelerVisibility.Owned
                : SavedTravelerVisibility.Foreign;
        });

    public Task<ErrorOr<SavedTravelerWriteOutcome>> CreateAsync(
        SavedTravelerStoredRecord record,
        CancellationToken ct
    ) =>
        Guard<SavedTravelerWriteOutcome>(async () =>
        {
            var invalid = ValidateRecord(record);
            if (invalid is { } error)
                return error;
            var entity = new SavedTravelerEntity
            {
                Id = record.Id.Value,
                OwnerUserId = record.OwnerUserId,
                Revision = record.Revision,
                ProtectedDetailsJson = JsonSerializer.Serialize(record.ProtectedDetails),
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
            };
            db.SavedTravelers.Add(entity);
            try
            {
                // Scoped EF commit only. No Wolverine session, messages or outbox.
                await db.SaveChangesAsync(ct);
                db.Entry(entity).State = EntityState.Detached;
                return SavedTravelerWriteOutcome.Succeeded;
            }
            catch (DbUpdateException ex)
                when (ex.InnerException
                        is PostgresException
                        {
                            SqlState: PostgresErrorCodes.UniqueViolation,
                            ConstraintName: "pk_saved_travelers"
                        }
                )
            {
                db.Entry(entity).State = EntityState.Detached;
                return await ClassifyMiss(record.OwnerUserId, record.Id, ct);
            }
            catch
            {
                // Never detach other tracked entities or leave our failed insert scheduled.
                db.Entry(entity).State = EntityState.Detached;
                throw;
            }
        });

    public Task<ErrorOr<SavedTravelerWriteOutcome>> UpdateAsync(
        SavedTravelerStoredRecord replacement,
        Guid expectedRevision,
        CancellationToken ct
    ) =>
        Guard<SavedTravelerWriteOutcome>(async () =>
        {
            var invalid = ValidateRecord(replacement);
            if (invalid is { } error)
                return error;
            if (expectedRevision == Guid.Empty)
                return SavedTravelerErrors.PreconditionInvalid;
            var json = JsonSerializer.Serialize(replacement.ProtectedDetails);
            var count = await db
                .SavedTravelers.Where(x =>
                    x.OwnerUserId == replacement.OwnerUserId
                    && x.Id == replacement.Id.Value
                    && x.Revision == expectedRevision
                )
                .ExecuteUpdateAsync(
                    set =>
                        set.SetProperty(x => x.Revision, replacement.Revision)
                            .SetProperty(x => x.ProtectedDetailsJson, json)
                            .SetProperty(x => x.UpdatedAt, replacement.UpdatedAt),
                    ct
                );
            return count == 1
                ? SavedTravelerWriteOutcome.Succeeded
                : await ClassifyMiss(replacement.OwnerUserId, replacement.Id, ct);
        });

    public Task<ErrorOr<SavedTravelerWriteOutcome>> DeleteAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        CancellationToken ct
    ) =>
        Guard<SavedTravelerWriteOutcome>(async () =>
        {
            var invalid = ValidateIdentity(ownerUserId, id);
            if (invalid is { } error)
                return error;
            if (expectedRevision == Guid.Empty)
                return SavedTravelerErrors.PreconditionInvalid;
            var count = await db
                .SavedTravelers.Where(x =>
                    x.OwnerUserId == ownerUserId
                    && x.Id == id.Value
                    && x.Revision == expectedRevision
                )
                .ExecuteDeleteAsync(ct);
            return count == 1
                ? SavedTravelerWriteOutcome.Succeeded
                : await ClassifyMiss(ownerUserId, id, ct);
        });

    private async Task<ErrorOr<SavedTravelerWriteOutcome>> ClassifyMiss(
        Guid owner,
        SavedTravelerId id,
        CancellationToken ct
    )
    {
        var visibility = await GetVisibilityAsync(owner, id, ct);
        if (visibility.IsError)
            return visibility.Errors;
        return visibility.Value == SavedTravelerVisibility.Owned
            ? SavedTravelerWriteOutcome.PreconditionFailed
            : SavedTravelerWriteOutcome.NotFound;
    }

    private static ErrorOr<SavedTravelerStoredRecord> ToRecord(SavedTravelerEntity entity)
    {
        try
        {
            // Corrupt envelope work is bounded before JSON parsing.
            if (
                entity.ProtectedDetailsJson is null
                || entity.ProtectedDetailsJson.Length
                    > ProtectedSavedTravelerSnapshot.MaxCiphertextLength + 1024
            )
                return PiiProtectionErrors.PayloadUnavailable;
            var envelope = JsonSerializer.Deserialize<ProtectedSavedTravelerSnapshot>(
                entity.ProtectedDetailsJson
            );
            var record = new SavedTravelerStoredRecord(
                new(entity.Id),
                entity.OwnerUserId,
                entity.Revision,
                envelope!,
                entity.CreatedAt,
                entity.UpdatedAt
            );
            return ValidateRecord(record) is not null
                ? PiiProtectionErrors.PayloadUnavailable
                : record;
        }
        catch (JsonException)
        {
            return PiiProtectionErrors.PayloadUnavailable;
        }
    }

    private static Error? ValidateRecord(SavedTravelerStoredRecord record)
    {
        var invalid = ValidateIdentity(record.OwnerUserId, record.Id);
        if (invalid is not null)
            return invalid;
        return
            record.Revision == Guid.Empty
            || record.ProtectedDetails is null
            || ProtectedSavedTravelerSnapshot
                .Create(record.ProtectedDetails.FormatVersion, record.ProtectedDetails.Ciphertext)
                .IsError
            ? PiiProtectionErrors.PayloadUnavailable
            : null;
    }

    private static Error? ValidateIdentity(Guid owner, SavedTravelerId id) =>
        owner == Guid.Empty ? SavedTravelerErrors.IdentityInvalid
        : id.Value == Guid.Empty ? SavedTravelerErrors.IdInvalid
        : null;

    private static bool IsTransientProviderWrapper(InvalidOperationException exception) =>
        exception.InnerException is { } inner
        // The nonretrying Npgsql strategy also wraps transient errors. Match its cause
        // predicate and EF's DbUpdateException unwrapping, not arbitrary exception chains.
        && ExecutionStrategy.CallOnWrappedException(
            inner,
            static cause => cause is TimeoutException or NpgsqlException { IsTransient: true }
        );

    private static async Task<ErrorOr<T>> Guard<T>(Func<Task<ErrorOr<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateException)
        {
            return SavedTravelerErrors.StorageUnavailable;
        }
        catch (DbException)
        {
            return SavedTravelerErrors.StorageUnavailable;
        }
        catch (InvalidOperationException ex) when (IsTransientProviderWrapper(ex))
        {
            return SavedTravelerErrors.StorageUnavailable;
        }
        catch (TimeoutException)
        {
            return SavedTravelerErrors.StorageUnavailable;
        }
        // Cancellation is intentionally propagated; no exception message or chain is returned.
    }
}
