using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record BookingPassenger
{
    public BookingPassengerId Id { get; }
    public BookingPassengerDetails Details { get; }

    [JsonConstructor]
    private BookingPassenger(BookingPassengerId id, BookingPassengerDetails details)
    {
        Id = id;
        Details = details;
    }

    public static ErrorOr<BookingPassenger> Create(
        BookingPassengerId id,
        BookingPassengerDetails details
    )
    {
        if (BookingPassengerId.Create(id.Value).IsError)
            return Error.Validation(
                "Flights.PassengerIdInvalid",
                "Passenger slot identity is required."
            );
        if (details is null)
            return BookingPassengerDetails.ForPassenger(
                [
                    Error.Validation(
                        "Flights.PassengerDetailsInvalid",
                        "Passenger details are required."
                    ),
                ],
                id
            );
        var validation = details.Validate();
        return validation.IsError
            ? BookingPassengerDetails.ForPassenger(validation.Errors, id)
            : new BookingPassenger(id, details);
    }

    public override string ToString() => "BookingPassenger";
}
