using System.Net.Mail;
using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record PassengerInfo
{
    public string GivenName { get; }
    public string FamilyName { get; }
    public DateOnly DateOfBirth { get; }
    public Gender Gender { get; }
    public string Email { get; }
    public PhoneNumber Phone { get; }

    [JsonConstructor]
    private PassengerInfo(
        string givenName,
        string familyName,
        DateOnly dateOfBirth,
        Gender gender,
        string email,
        PhoneNumber phone
    )
    {
        GivenName = givenName;
        FamilyName = familyName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Email = email;
        Phone = phone;
    }

    public static ErrorOr<PassengerInfo> Create(
        string givenName,
        string familyName,
        DateOnly dateOfBirth,
        Gender gender,
        string email,
        PhoneNumber phone
    )
    {
        var trimmedGiven = givenName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedGiven))
            return Error.Validation(
                "PassengerInfo.GivenNameEmpty",
                "Given name must not be blank."
            );

        var trimmedFamily = familyName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedFamily))
            return Error.Validation(
                "PassengerInfo.FamilyNameEmpty",
                "Family name must not be blank."
            );

        if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            return Error.Validation(
                "PassengerInfo.DateOfBirthFuture",
                "Date of birth must not be in the future."
            );

        if (!IsValidEmail(email))
            return Error.Validation("PassengerInfo.EmailInvalid", "Email address is not valid.");

        return new PassengerInfo(trimmedGiven, trimmedFamily, dateOfBirth, gender, email, phone);
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
