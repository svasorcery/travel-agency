using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Endpoints;

public static class FlightsPassengerProblemDetails
{
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["Flights.PassengerTitleInvalid"] = "title",
        ["Flights.PassengerGivenNameInvalid"] = "givenName",
        ["Flights.PassengerFamilyNameInvalid"] = "familyName",
        ["Flights.PassengerGenderInvalid"] = "gender",
        ["Flights.PassengerEmailInvalid"] = "email",
        ["Flights.PassengerPhoneInvalid"] = "phone",
        ["Flights.PassengerDateOfBirthInvalid"] = "dateOfBirth",
        ["Flights.PassengerDateOfBirthFutureInvalid"] = "dateOfBirth",
        ["Flights.PassengerAdultRequiredInvalid"] = "dateOfBirth",
    };

    public static ProblemDetails From(List<Error> errors)
    {
        if (
            !errors.Any(e =>
                Fields.ContainsKey(e.Code) || e.Code == "Flights.PassengerDetailsInvalid"
            )
        )
            return errors.ToProblemDetails();
        var problem = new List<Error>
        {
            Error.Validation("Flights.PassengerInvalid", "Passenger details are invalid."),
        }.ToProblemDetails();
        var safe = errors
            .Where(e =>
                Fields.TryGetValue(e.Code, out var field)
                && e.Metadata is not null
                && e.Metadata.TryGetValue("bookingPassengerId", out var rawId)
                && rawId is Guid id
                && id != Guid.Empty
                && e.Metadata.TryGetValue("field", out var rawField)
                && rawField is string suppliedField
                && suppliedField == field
            )
            .Take(63)
            .Select(e => new
            {
                bookingPassengerId = (Guid)e.Metadata!["bookingPassengerId"],
                field = Fields[e.Code],
                code = e.Code,
            })
            .ToArray();
        if (safe.Length > 0)
            problem.Extensions["passengerErrors"] = safe;
        return problem;
    }
}
