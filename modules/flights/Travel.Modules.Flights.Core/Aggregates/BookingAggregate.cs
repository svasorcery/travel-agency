using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Exceptions;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

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
    public IReadOnlyList<string> TicketNumbers { get; private set; } = Array.Empty<string>();
    public string? ProviderOfferRef { get; private set; }

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
    }

    public void Apply(OrderTicketed e)
    {
        Status = BookingStatus.Ticketed;
        TicketNumbers = e.TicketNumbers;
    }

    public void Apply(OrderCancelled e)
    {
        Status = BookingStatus.Cancelled;
    }

    public void Apply(OrderRefunded e)
    {
        Status = BookingStatus.Refunded;
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
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            throw new InvalidBookingStateException(
                $"Cannot cancel when booking is in state {Status}."
            );
    }

    public void GuardOfferNotExpired(TimeProvider time)
    {
        if (ExpiresAt is { } e && e <= time.GetUtcNow())
            throw new InvalidBookingStateException("Offer has expired.");
    }
}
