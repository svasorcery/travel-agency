using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Application.SavedTravelers;

public interface ISavedTravelerService
{
    Task<ErrorOr<SavedTravelerPage>> ListAsync(Guid ownerUserId, int offset, CancellationToken ct);
    Task<ErrorOr<SavedTravelerView>> GetAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    );
    Task<ErrorOr<SavedTravelerReceipt>> CreateAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        SavedTravelerDetails details,
        CancellationToken ct
    );
    Task<ErrorOr<SavedTravelerReceipt>> UpdateAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        SavedTravelerDetails details,
        CancellationToken ct
    );
    Task<ErrorOr<Success>> DeleteAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        CancellationToken ct
    );
}
