using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Application.SavedTravelers;

public interface ISavedTravelerStore
{
    Task<ErrorOr<SavedTravelerStoredRecord?>> GetOwnedAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    );
    Task<ErrorOr<IReadOnlyList<SavedTravelerStoredRecord>>> ListOwnedAsync(
        Guid ownerUserId,
        int offset,
        CancellationToken ct
    );
    Task<ErrorOr<SavedTravelerVisibility>> GetVisibilityAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    );
    Task<ErrorOr<SavedTravelerWriteOutcome>> CreateAsync(
        SavedTravelerStoredRecord record,
        CancellationToken ct
    );
    Task<ErrorOr<SavedTravelerWriteOutcome>> UpdateAsync(
        SavedTravelerStoredRecord replacement,
        Guid expectedRevision,
        CancellationToken ct
    );
    Task<ErrorOr<SavedTravelerWriteOutcome>> DeleteAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        CancellationToken ct
    );
}

public enum SavedTravelerVisibility
{
    Missing,
    Owned,
    Foreign,
}

public enum SavedTravelerWriteOutcome
{
    Succeeded,
    PreconditionFailed,
    NotFound,
}

public sealed record SavedTravelerStoredRecord(
    SavedTravelerId Id,
    Guid OwnerUserId,
    Guid Revision,
    ProtectedSavedTravelerSnapshot ProtectedDetails,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
)
{
    public override string ToString() => nameof(SavedTravelerStoredRecord);
}
