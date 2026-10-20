using ErrorOr;

namespace Travel.Modules.Trips.Core;

public static class TripErrors
{
    public static Error InvalidInput =>
        Error.Validation("Trips.InvalidInput", "The trip request is invalid.");
    public static Error Forbidden =>
        Error.Forbidden("Trips.Forbidden", "Trip access is forbidden.");
    public static Error NotFound =>
        Error.NotFound("Trips.NotFound", "The trip plan was not found.");
    public static Error Unavailable =>
        Error.Failure("Trips.Unavailable", "Trips is temporarily unavailable.");
    public static Error RevisionMismatch =>
        Error.Conflict("Trips.RevisionMismatch", "The saved revision has changed.");
    public static Error CapacityExceeded =>
        Error.Conflict("Trips.CapacityExceeded", "The owner plan capacity has been reached.");
    public static Error DateRangeWouldDropBlocks =>
        Error.Conflict(
            "Trips.DateRangeWouldDropBlocks",
            "Move or remove blocks before shortening the date range."
        );
    public static Error BriefChangeRequiresEmptyPlan =>
        Error.Conflict(
            "Trips.BriefChangeRequiresEmptyPlan",
            "Clear blocks before changing destination or currency."
        );
    public static Error RevisionExhausted =>
        Error.Conflict("Trips.RevisionExhausted", "This plan cannot accept further revisions.");
    public static Error PlanTooLarge =>
        Error.Validation("Trips.PlanTooLarge", "The complete trip plan exceeds the size limit.");
    public static Error InvalidCursor =>
        Error.Validation("Trips.InvalidCursor", "The trip list cursor is invalid.");
}
