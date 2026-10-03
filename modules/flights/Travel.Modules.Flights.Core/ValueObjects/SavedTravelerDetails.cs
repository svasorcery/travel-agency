using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record SavedTravelerDetails
{
    public PassengerInfo Passenger { get; }
    public PassengerTitle Title { get; }

    [JsonConstructor]
    private SavedTravelerDetails(PassengerInfo passenger, PassengerTitle title)
    {
        Passenger = passenger;
        Title = title;
    }

    public static ErrorOr<SavedTravelerDetails> Create(
        PassengerInfo passenger,
        PassengerTitle title
    )
    {
        var details = new SavedTravelerDetails(passenger, title);
        var validation = details.Validate();
        return validation.IsError ? validation.Errors : details;
    }

    public static ErrorOr<SavedTravelerDetails> CreateRaw(
        string title,
        string givenName,
        string familyName,
        DateOnly dateOfBirth,
        string gender,
        string email,
        string phone,
        DateOnly today
    )
    {
        var validated = BookingPassengerDetails.CreateRaw(
            title,
            givenName,
            familyName,
            dateOfBirth,
            gender,
            email,
            phone,
            today
        );
        return validated.IsError
            ? validated.Errors
            : new SavedTravelerDetails(validated.Value.Passenger, validated.Value.Title);
    }

    public ErrorOr<Success> Validate()
    {
        var result = BookingPassengerDetails.Create(Passenger, Title);
        return result.IsError ? result.Errors : Result.Success;
    }

    public ErrorOr<Success> ValidateForProfile(DateOnly today)
    {
        var validation = Validate();
        if (validation.IsError)
            return validation.Errors;
        return Passenger.DateOfBirth > today
            ? Error.Validation(
                "Flights.PassengerDateOfBirthFutureInvalid",
                "Date of birth must not be in the future."
            )
            : Result.Success;
    }

    public override string ToString() => "SavedTravelerDetails";
}
