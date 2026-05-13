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

    // Marten convention: parameterless ctor + Apply methods (added in Task 9)
    public BookingAggregate() { }
}
