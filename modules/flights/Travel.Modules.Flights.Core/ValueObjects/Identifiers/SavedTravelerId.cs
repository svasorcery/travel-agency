using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct SavedTravelerId(Guid Value)
{
    public static ErrorOr<SavedTravelerId> Create(Guid value) =>
        value != Guid.Empty
            ? new SavedTravelerId(value)
            : Error.Validation(
                "Flights.SavedTravelerIdInvalid",
                "Saved traveler identity is required."
            );
}
