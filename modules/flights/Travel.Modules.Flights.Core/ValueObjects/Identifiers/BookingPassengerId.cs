using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

/// <summary>A quote-local passenger identity, never an owner or saved profile identity.</summary>
public readonly record struct BookingPassengerId(Guid Value)
{
    public static ErrorOr<BookingPassengerId> Create(Guid value) =>
        value != Guid.Empty
            ? new BookingPassengerId(value)
            : Error.Validation(
                "Flights.PassengerIdInvalid",
                "Passenger slot identity is required."
            );
}
