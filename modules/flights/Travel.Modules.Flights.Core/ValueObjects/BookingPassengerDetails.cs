using System.Net.Mail;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record BookingPassengerDetails
{
    public PassengerInfo Passenger { get; }
    public PassengerTitle Title { get; }

    [JsonConstructor]
    private BookingPassengerDetails(PassengerInfo passenger, PassengerTitle title)
    {
        Passenger = passenger;
        Title = title;
    }

    public static ErrorOr<BookingPassengerDetails> Create(
        PassengerInfo passenger,
        PassengerTitle title
    )
    {
        var details = new BookingPassengerDetails(passenger, title);
        var validation = details.Validate();
        return validation.IsError ? validation.Errors : details;
    }

    public static ErrorOr<BookingPassengerDetails> CreateRaw(
        BookingPassengerId id,
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
        Error? error = null;
        if (id.Value == Guid.Empty)
            return Error.Validation("Flights.PassengerIdInvalid", "Passenger slot ID is required.");
        if (!ValidName(givenName))
            error = Invalid("GivenName", "Given name is invalid.");
        else if (!ValidName(familyName))
            error = Invalid("FamilyName", "Family name is invalid.");
        else if (gender is not ("male" or "female"))
            error = Invalid("Gender", "Select male or female explicitly.");
        else if (PassengerTitle.Create(title).IsError)
            error = Invalid("Title", "Select a supported passenger title.");
        else if (!ValidEmail(email))
            error = Invalid("Email", "Passenger email is invalid.");
        else if (!ValidPhone(phone))
            error = Invalid("Phone", "Passenger phone must be an ASCII E.164 number.");
        else if (dateOfBirth == DateOnly.MinValue)
            error = Invalid("DateOfBirth", "Date of birth is required.");
        else if (dateOfBirth > today)
            error = Invalid("DateOfBirthFuture", "Date of birth must not be in the future.");
        if (error is { } invalid)
            return ForPassenger([invalid], id);
        var passenger = PassengerInfo.Create(
            givenName,
            familyName,
            dateOfBirth,
            gender == "male" ? Gender.Male : Gender.Female,
            email,
            PhoneNumber.Create(phone).Value,
            today
        );
        if (passenger.IsError)
            return ForPassenger([Invalid("Details", "Passenger details are invalid.")], id);
        return Create(passenger.Value, PassengerTitle.Create(title).Value);
    }

    private static bool ValidPhone(string? phone) =>
        phone is { Length: >= 9 and <= 16 }
        && phone[0] == '+'
        && phone[1] is >= '1' and <= '9'
        && phone.Skip(2).All(c => c is >= '0' and <= '9');

    // Repeated after JSON construction: legacy value-object constructors intentionally preserve history.
    public ErrorOr<Success> Validate()
    {
        if (Passenger is null)
            return Invalid("Details", "Passenger details are required.");
        if (!ValidName(Passenger.GivenName))
            return Invalid("GivenName", "Given name must have 1 to 20 supported Latin characters.");
        if (!ValidName(Passenger.FamilyName))
            return Invalid(
                "FamilyName",
                "Family name must have 1 to 20 supported Latin characters."
            );
        if (Passenger.Gender?.Code is not ("male" or "female"))
            return Invalid("Gender", "Select male or female explicitly.");
        if (Title is null || PassengerTitle.Create(Title.Code).IsError)
            return Invalid("Title", "Select a supported passenger title.");
        if (!ValidEmail(Passenger.Email))
            return Invalid("Email", "Passenger email is invalid or exceeds 254 characters.");
        if (!ValidPhone(Passenger.Phone?.Value))
            return Invalid("Phone", "Passenger phone must be an ASCII E.164 number.");
        if (Passenger.DateOfBirth == DateOnly.MinValue)
            return Invalid("DateOfBirth", "Date of birth is required.");
        return Result.Success;
    }

    public ErrorOr<Success> ValidateForTravel(DateOnly firstDepartureLocalDate, DateOnly today)
    {
        var validation = Validate();
        if (validation.IsError)
            return validation.Errors;
        if (Passenger.DateOfBirth > today)
            return Invalid("DateOfBirthFuture", "Date of birth must not be in the future.");
        // Demo convention: AddYears maps February 29 to February 28 in a non-leap year.
        if (
            Passenger.DateOfBirth.Year > 9981
            || Passenger.DateOfBirth.AddYears(18) > firstDepartureLocalDate
        )
            return Invalid(
                "AdultRequired",
                "Passenger must be at least 18 on the first departure date."
            );
        return Result.Success;
    }

    internal static List<Error> ForPassenger(List<Error> errors, BookingPassengerId id) =>
        errors
            .Select(error =>
            {
                var field = error.Code switch
                {
                    "Flights.PassengerGivenNameInvalid" => "givenName",
                    "Flights.PassengerFamilyNameInvalid" => "familyName",
                    "Flights.PassengerGenderInvalid" => "gender",
                    "Flights.PassengerTitleInvalid" => "title",
                    "Flights.PassengerEmailInvalid" => "email",
                    "Flights.PassengerPhoneInvalid" => "phone",
                    "Flights.PassengerDateOfBirthInvalid"
                    or "Flights.PassengerDateOfBirthFutureInvalid"
                    or "Flights.PassengerAdultRequiredInvalid" => "dateOfBirth",
                    _ => "details",
                };
                return Error.Validation(
                    error.Code,
                    error.Description,
                    new Dictionary<string, object>
                    {
                        ["bookingPassengerId"] = id.Value,
                        ["field"] = field,
                    }
                );
            })
            .ToList();

    private static Error Invalid(string field, string message) =>
        Error.Validation($"Flights.Passenger{field}Invalid", message);

    private static bool ValidName(string? value) =>
        value is { Length: >= 1 and <= 20 }
        && value.Any(char.IsLetter)
        && value.All(c =>
            c is ' ' or '-' or '\''
            || (
                (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00c0' and <= '\u017f')
                && char.IsLetter(c)
                && !"ÆæĲĳŒœÞð".Contains(c)
            )
        );

    private static bool ValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value != value.Trim())
            return false;
        return MailAddress.TryCreate(value, out var address) && address.Address == value;
    }

    public override string ToString() => "BookingPassengerDetails";
}
