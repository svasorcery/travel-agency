using ErrorOr;

namespace Travel.Modules.Flights.Application.Privacy;

public static class PiiProtectionErrors
{
    public static Error Unavailable =>
        Error.Custom(
            503,
            "Flights.PiiProtectionUnavailable",
            "Passenger data protection is unavailable."
        );
    public static Error PayloadUnavailable =>
        Error.Custom(503, "Flights.PiiPayloadUnavailable", "Protected data is unavailable.");
    public static Error InvalidEnvelope =>
        Error.Validation("Flights.PiiEnvelopeInvalid", "Protected data format is invalid.");
    public static Error AlreadyInitialized =>
        Error.Conflict(
            "Flights.PiiKeyRingAlreadyInitialized",
            "The key ring is not empty; it will not be replaced."
        );
}
