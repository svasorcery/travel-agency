using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Privacy;

public sealed record SavedTravelerProtectionContext(
    Guid OwnerUserId,
    Guid TravelerId,
    Guid Revision
);

public interface ISavedTravelerProtector
{
    ErrorOr<ProtectedSavedTravelerSnapshot> Protect(
        SavedTravelerProtectionContext context,
        SavedTravelerDetails details
    );
    ErrorOr<SavedTravelerDetails> Unprotect(
        SavedTravelerProtectionContext context,
        ProtectedSavedTravelerSnapshot snapshot
    );
}
