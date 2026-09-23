namespace Travel.Modules.Flights.Core.Aggregates;

public enum BookingTransition
{
    ReQuote,
    Hold,
    Confirm,
    Cancel,
    Ticket,
    Refund,
}

public enum BookingRejectionCode
{
    InvalidState,
    OfferExpired,
    HoldExpired,
    OrderAlreadyTicketed,
    PrerequisiteNotMet,
    OwnerMissing,
    OwnerConflict,
    OfferReferenceMismatch,
}

public sealed record BookingRejection(
    BookingTransition Transition,
    BookingRejectionCode Code,
    BookingStatus Status
);

public abstract record BookingTransitionDecision
{
    public sealed record Allowed : BookingTransitionDecision;

    public sealed record IdempotentNoOp(BookingStatus Status) : BookingTransitionDecision;

    public sealed record Rejected(BookingRejection Reason) : BookingTransitionDecision;
}
