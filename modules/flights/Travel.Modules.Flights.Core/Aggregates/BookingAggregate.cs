using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public enum BookingStatus
{
    None = 0,
    OfferQuoted,
    Held,
    Confirmed,
    Ticketed,
    Cancelled,
    Refunded,
}

public sealed class BookingAggregate
{
    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public BookingStatus Status { get; private set; } = BookingStatus.None;
    public OfferId? OfferId { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public Itinerary? Itinerary { get; private set; }
    public Money? TotalAmount { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public PassengerInfo? Passenger { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public PaymentRef? PaymentRef { get; private set; }
    public EquatableArray<string> TicketNumbers { get; private set; } = new([]);
    public string? ProviderOfferRef { get; private set; }
    public FareConditions? FareConditions { get; private set; }

    // ── Event-sourced timestamps (read-model projection reads these, not the clock) ─
    public DateTimeOffset? BookedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? TicketedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    // Marten convention: parameterless ctor
    public BookingAggregate() { }

    // ── Apply methods (Marten stream-rebuild convention) ─────────────────────

    public void Apply(OfferQuoted e)
    {
        Status = BookingStatus.OfferQuoted;
        OfferId = e.OfferId;
        Itinerary = e.Itinerary;
        TotalAmount = e.TotalAmount;
        ExpiresAt = e.ExpiresAt;
        ProviderOfferRef = e.ProviderRef;
        FareConditions = e.FareConditions;
    }

    public void Apply(OfferReQuoted e)
    {
        if (e.RefreshedOffer is null)
        {
            TotalAmount = e.NewAmount;
            return;
        }

        OfferId = e.RefreshedOffer.Id;
        Itinerary = e.RefreshedOffer.Itinerary;
        TotalAmount = e.RefreshedOffer.TotalAmount;
        ExpiresAt = e.RefreshedOffer.ExpiresAt;
        ProviderOfferRef = e.RefreshedOffer.ProviderOfferRef;
        FareConditions = e.RefreshedOffer.FareConditions;
    }

    public void Apply(OfferHeld e)
    {
        Status = BookingStatus.Held;
        ProviderOrderId = e.OrderId;
        Passenger = e.Passenger;
        OwnerUserId = e.OwnerUserId;
        ExpiresAt = e.HeldUntil;
        BookedAt = e.HeldAt;
    }

    public void Apply(PaymentAuthorized e)
    {
        PaymentRef = e.PaymentRef;
    }

    public void Apply(OrderConfirmed e)
    {
        Status = BookingStatus.Confirmed;
        ProviderOrderId = e.OrderId;
        PaymentRef = e.PaymentRef;
        ConfirmedAt = e.ConfirmedAt;
    }

    public void Apply(OrderTicketed e)
    {
        Status = BookingStatus.Ticketed;
        TicketNumbers = e.TicketNumbers;
        TicketedAt = e.TicketedAt;
    }

    public void Apply(OrderCancelled e)
    {
        Status = BookingStatus.Cancelled;
        CancelledAt = e.CancelledAt;
    }

    public void Apply(OrderRefunded e)
    {
        Status = BookingStatus.Refunded;
        RefundedAt = e.RefundedAt;
    }

    // ── Transition decisions ──────────────────────────────────────────────────

    public BookingTransitionDecision DecideReQuote(string providerOfferRef)
    {
        if (Status is not BookingStatus.OfferQuoted)
            return Reject(BookingTransition.ReQuote, BookingRejectionCode.InvalidState);

        return string.Equals(ProviderOfferRef, providerOfferRef, StringComparison.Ordinal)
            ? new BookingTransitionDecision.Allowed()
            : Reject(BookingTransition.ReQuote, BookingRejectionCode.OfferReferenceMismatch);
    }

    public BookingTransitionDecision DecideHold(DateTimeOffset now)
    {
        if (Status is not BookingStatus.OfferQuoted)
            return Reject(BookingTransition.Hold, BookingRejectionCode.InvalidState);

        return ExpiresAt is { } expiresAt && expiresAt <= now
            ? Reject(BookingTransition.Hold, BookingRejectionCode.OfferExpired)
            : new BookingTransitionDecision.Allowed();
    }

    public BookingTransitionDecision DecideConfirm(DateTimeOffset now)
    {
        if (Status is not BookingStatus.Held)
            return Reject(BookingTransition.Confirm, BookingRejectionCode.InvalidState);

        return ExpiresAt is { } heldUntil && heldUntil <= now
            ? Reject(BookingTransition.Confirm, BookingRejectionCode.HoldExpired)
            : new BookingTransitionDecision.Allowed();
    }

    public BookingTransitionDecision DecideCancel() =>
        Status switch
        {
            BookingStatus.Held or BookingStatus.Confirmed =>
                new BookingTransitionDecision.Allowed(),
            BookingStatus.Cancelled or BookingStatus.Refunded =>
                new BookingTransitionDecision.IdempotentNoOp(Status),
            BookingStatus.Ticketed => Reject(
                BookingTransition.Cancel,
                BookingRejectionCode.OrderAlreadyTicketed
            ),
            _ => Reject(BookingTransition.Cancel, BookingRejectionCode.InvalidState),
        };

    public BookingTransitionDecision DecideTicket() =>
        Status switch
        {
            BookingStatus.Confirmed => new BookingTransitionDecision.Allowed(),
            BookingStatus.Ticketed or BookingStatus.Cancelled or BookingStatus.Refunded =>
                new BookingTransitionDecision.IdempotentNoOp(Status),
            BookingStatus.Held => Reject(
                BookingTransition.Ticket,
                BookingRejectionCode.PrerequisiteNotMet
            ),
            _ => Reject(BookingTransition.Ticket, BookingRejectionCode.InvalidState),
        };

    public BookingTransitionDecision DecideRefund() =>
        Status switch
        {
            BookingStatus.Confirmed or BookingStatus.Ticketed =>
                new BookingTransitionDecision.Allowed(),
            BookingStatus.Cancelled or BookingStatus.Refunded =>
                new BookingTransitionDecision.IdempotentNoOp(Status),
            BookingStatus.Held => Reject(
                BookingTransition.Refund,
                BookingRejectionCode.PrerequisiteNotMet
            ),
            _ => Reject(BookingTransition.Refund, BookingRejectionCode.InvalidState),
        };

    public BookingTransitionDecision DecideOwner(BookingTransition transition, Guid userId)
    {
        if (userId == Guid.Empty)
            return Reject(transition, BookingRejectionCode.OwnerMissing);

        if (OwnerUserId is null)
        {
            return transition is BookingTransition.Hold && Status is BookingStatus.OfferQuoted
                ? new BookingTransitionDecision.Allowed()
                : Reject(transition, BookingRejectionCode.OwnerMissing);
        }

        return OwnerUserId == userId
            ? new BookingTransitionDecision.Allowed()
            : Reject(transition, BookingRejectionCode.OwnerConflict);
    }

    private BookingTransitionDecision.Rejected Reject(
        BookingTransition transition,
        BookingRejectionCode code
    ) => new(new BookingRejection(transition, code, Status));
}
