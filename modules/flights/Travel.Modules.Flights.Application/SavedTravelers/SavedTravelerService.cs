using ErrorOr;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Application.SavedTravelers;

public sealed class SavedTravelerService(
    ISavedTravelerStore store,
    ISavedTravelerProtector protector,
    TimeProvider time
) : ISavedTravelerService
{
    public async Task<ErrorOr<SavedTravelerPage>> ListAsync(
        Guid ownerUserId,
        int offset,
        CancellationToken ct
    )
    {
        if (ownerUserId == Guid.Empty)
            return SavedTravelerErrors.IdentityInvalid;
        if (!SavedTravelerPaging.IsValid(offset))
            return SavedTravelerErrors.PageInvalid;
        var result = await store.ListOwnedAsync(ownerUserId, offset, ct);
        if (result.IsError)
            return result.Errors;
        var rows = result.Value;
        if (rows is null || rows.Count > SavedTravelerPaging.ReadSize)
            return PiiProtectionErrors.PayloadUnavailable;
        var ids = new HashSet<Guid>();
        // Validate the complete opaque lookahead before decrypting any displayed row.
        foreach (var row in rows)
            if (!IsValidRecord(row, ownerUserId) || !ids.Add(row.Id.Value))
                return PiiProtectionErrors.PayloadUnavailable;
        var views = new List<SavedTravelerView>(SavedTravelerPaging.PageSize);
        foreach (var row in rows.Take(SavedTravelerPaging.PageSize))
        {
            var view = Read(row);
            if (view.IsError)
                return view.Errors;
            views.Add(view.Value);
        }
        return new SavedTravelerPage(
            views.AsReadOnly(),
            offset,
            rows.Count > SavedTravelerPaging.PageSize
        );
    }

    public async Task<ErrorOr<SavedTravelerView>> GetAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    )
    {
        var invalid = ValidateIdentity(ownerUserId, id);
        if (invalid is { } error)
            return error;
        var result = await store.GetOwnedAsync(ownerUserId, id, ct);
        if (result.IsError)
            return result.Errors;
        if (result.Value is not { } row || row.OwnerUserId != ownerUserId)
            return SavedTravelerErrors.NotFound;
        if (row.Id != id || !IsValidRecord(row, ownerUserId))
            return PiiProtectionErrors.PayloadUnavailable;
        return Read(row);
    }

    public async Task<ErrorOr<SavedTravelerReceipt>> CreateAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        SavedTravelerDetails details,
        CancellationToken ct
    )
    {
        var invalid = ValidateIdentity(ownerUserId, id);
        if (invalid is { } error)
            return error;
        var visibility = await store.GetVisibilityAsync(ownerUserId, id, ct);
        if (visibility.IsError)
            return visibility.Errors;
        if (visibility.Value == SavedTravelerVisibility.Owned)
            return SavedTravelerErrors.PreconditionFailed;
        if (visibility.Value != SavedTravelerVisibility.Missing)
            return SavedTravelerErrors.NotFound;
        var now = time.GetUtcNow();
        var validation = details.ValidateForProfile(DateOnly.FromDateTime(now.UtcDateTime));
        if (validation.IsError)
            return validation.Errors;
        var revision = Guid.NewGuid();
        var protectedDetails = protector.Protect(new(ownerUserId, id.Value, revision), details);
        if (protectedDetails.IsError)
            return protectedDetails.Errors;
        if (!IsValidEnvelope(protectedDetails.Value))
            return PiiProtectionErrors.PayloadUnavailable;
        var written = await store.CreateAsync(
            new(id, ownerUserId, revision, protectedDetails.Value, now, now),
            ct
        );
        if (written.IsError)
            return written.Errors;
        var failure = WriteFailure(written.Value);
        return failure is { } refused ? refused : new SavedTravelerReceipt(id, revision);
    }

    public async Task<ErrorOr<SavedTravelerReceipt>> UpdateAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        SavedTravelerDetails details,
        CancellationToken ct
    )
    {
        var invalid = ValidateIdentity(ownerUserId, id, expectedRevision);
        if (invalid is { } error)
            return error;
        var result = await store.GetOwnedAsync(ownerUserId, id, ct);
        if (result.IsError)
            return result.Errors;
        if (result.Value is not { } row || row.OwnerUserId != ownerUserId)
            return SavedTravelerErrors.NotFound;
        if (row.Id != id || !IsValidRecord(row, ownerUserId))
            return PiiProtectionErrors.PayloadUnavailable;
        if (row.Revision != expectedRevision)
            return SavedTravelerErrors.PreconditionFailed;
        var now = time.GetUtcNow();
        var validation = details.ValidateForProfile(DateOnly.FromDateTime(now.UtcDateTime));
        if (validation.IsError)
            return validation.Errors;
        var revision = Guid.NewGuid();
        var protectedDetails = protector.Protect(new(ownerUserId, id.Value, revision), details);
        if (protectedDetails.IsError)
            return protectedDetails.Errors;
        if (!IsValidEnvelope(protectedDetails.Value))
            return PiiProtectionErrors.PayloadUnavailable;
        var written = await store.UpdateAsync(
            new(id, ownerUserId, revision, protectedDetails.Value, row.CreatedAt, now),
            expectedRevision,
            ct
        );
        if (written.IsError)
            return written.Errors;
        var failure = WriteFailure(written.Value);
        return failure is { } refused ? refused : new SavedTravelerReceipt(id, revision);
    }

    public async Task<ErrorOr<Success>> DeleteAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        CancellationToken ct
    )
    {
        var invalid = ValidateIdentity(ownerUserId, id, expectedRevision);
        if (invalid is { } error)
            return error;
        var written = await store.DeleteAsync(ownerUserId, id, expectedRevision, ct);
        if (written.IsError)
            return written.Errors;
        var failure = WriteFailure(written.Value);
        return failure is { } refused ? refused : Result.Success;
    }

    private ErrorOr<SavedTravelerView> Read(SavedTravelerStoredRecord row)
    {
        var details = protector.Unprotect(
            new(row.OwnerUserId, row.Id.Value, row.Revision),
            row.ProtectedDetails
        );
        if (details.IsError || details.Value is null)
            return PiiProtectionErrors.PayloadUnavailable;
        if (
            details
                .Value.ValidateForProfile(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
                .IsError
        )
            return PiiProtectionErrors.PayloadUnavailable;
        return new SavedTravelerView(row.Id, row.Revision, details.Value);
    }

    private static bool IsValidRecord(SavedTravelerStoredRecord? row, Guid owner) =>
        row is not null
        && row.Id.Value != Guid.Empty
        && row.OwnerUserId == owner
        && row.Revision != Guid.Empty
        && IsValidEnvelope(row.ProtectedDetails);

    private static bool IsValidEnvelope(ProtectedSavedTravelerSnapshot? envelope) =>
        envelope is not null
        && !ProtectedSavedTravelerSnapshot
            .Create(envelope.FormatVersion, envelope.Ciphertext)
            .IsError;

    private static Error? ValidateIdentity(Guid owner, SavedTravelerId id, Guid? revision = null) =>
        owner == Guid.Empty ? SavedTravelerErrors.IdentityInvalid
        : id.Value == Guid.Empty ? SavedTravelerErrors.IdInvalid
        : revision == Guid.Empty ? SavedTravelerErrors.PreconditionInvalid
        : null;

    private static Error? WriteFailure(SavedTravelerWriteOutcome outcome) =>
        outcome switch
        {
            SavedTravelerWriteOutcome.Succeeded => null,
            SavedTravelerWriteOutcome.PreconditionFailed => SavedTravelerErrors.PreconditionFailed,
            SavedTravelerWriteOutcome.NotFound => SavedTravelerErrors.NotFound,
            _ => SavedTravelerErrors.StorageUnavailable,
        };
}
