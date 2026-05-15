using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Exceptions;
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
        TotalAmount = e.NewAmount;
    }

    public void Apply(OfferHeld e)
    {
        Status = BookingStatus.Held;
        ProviderOrderId = e.OrderId;
        Passenger = e.Passenger;
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

    // ── Transition guards (used by command handlers) ──────────────────────────

    public void GuardCanHold()
    {
        if (Status is not BookingStatus.OfferQuoted)
            throw new InvalidBookingStateException(
                $"Cannot hold an offer when booking is in state {Status}."
            );
    }

    public void GuardCanConfirm()
    {
        if (Status is not BookingStatus.Held)
            throw new InvalidBookingStateException(
                $"Cannot confirm when booking is in state {Status}."
            );
    }

    public void GuardCanCancel()
    {
        // Ticketed is terminal-for-cancel: tickets have been issued and any refund
        // must go through the airline's webhook-driven OrderRefunded flow
        // (foundation spec §4.1).
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded or BookingStatus.Ticketed)
            throw new InvalidBookingStateException(
                $"Cannot cancel when booking is in state {Status}."
            );
    }

    /// <summary>
    /// A webhook-driven OrderTicketed may only be appended once: replays or a
    /// late-arriving "documents issued" callback for an already-ticketed/cancelled/
    /// refunded stream must be a no-op (see WHK-C1, foundation spec §4.1).
    /// </summary>
    public void GuardCanTicket()
    {
        if (Status is not BookingStatus.Confirmed)
            throw new InvalidBookingStateException($"Cannot ticket in state {Status}.");
    }

    /// <summary>
    /// A webhook-driven OrderRefunded may only land on a non-terminal stream:
    /// once Cancelled or Refunded, further airline-initiated-change.cancelled
    /// events for the same order must be ignored (poison-message avoidance —
    /// we do not want webhook retries to keep re-refunding) (WHK-C2/C3, §4.1).
    /// </summary>
    public void GuardCanRefund()
    {
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            throw new InvalidBookingStateException($"Cannot refund in state {Status}.");
    }

    public void GuardOfferNotExpired(TimeProvider time)
    {
        if (ExpiresAt is { } e && e <= time.GetUtcNow())
            throw new InvalidBookingStateException("Offer has expired.");
    }
}
