using ErrorOr;

namespace Travel.Modules.Flights.Application.SavedTravelers;

public static class SavedTravelerErrors
{
    public static Error NotFound =>
        Error.NotFound("Flights.TravelerNotFound", "Saved traveler was not found.");
    public static Error PreconditionFailed =>
        Error.Custom(412, "Flights.TravelerPreconditionFailed", "Saved traveler has changed.");
    public static Error PageInvalid =>
        Error.Validation("Flights.TravelerPageInvalid", "Saved traveler page is invalid.");
    public static Error IdInvalid =>
        Error.Validation("Flights.TravelerIdInvalid", "Saved traveler identifier is invalid.");
    public static Error IdentityInvalid =>
        Error.Validation("Flights.TravelerIdentityInvalid", "Saved traveler owner is invalid.");
    public static Error PreconditionRequired =>
        Error.Custom(
            428,
            "Flights.TravelerPreconditionRequired",
            "A saved traveler precondition is required."
        );
    public static Error PreconditionInvalid =>
        Error.Validation(
            "Flights.TravelerPreconditionInvalid",
            "Saved traveler precondition is invalid."
        );
    public static Error StorageUnavailable =>
        Error.Custom(
            503,
            "Flights.TravelerStorageUnavailable",
            "Saved traveler storage is unavailable."
        );
}
