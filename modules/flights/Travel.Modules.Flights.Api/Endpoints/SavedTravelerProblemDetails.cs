using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Endpoints;

public static class SavedTravelerProblemDetails
{
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["Flights.PassengerTitleInvalid"] = "title",
        ["Flights.PassengerGivenNameInvalid"] = "givenName",
        ["Flights.PassengerFamilyNameInvalid"] = "familyName",
        ["Flights.PassengerDateOfBirthInvalid"] = "dateOfBirth",
        ["Flights.PassengerDateOfBirthFutureInvalid"] = "dateOfBirth",
        ["Flights.PassengerGenderInvalid"] = "gender",
        ["Flights.PassengerEmailInvalid"] = "email",
        ["Flights.PassengerPhoneInvalid"] = "phone",
    };

    public static ProblemDetails Invalid() =>
        new List<Error>
        {
            Error.Validation("Flights.TravelerInvalid", "Saved traveler details are invalid."),
        }.ToProblemDetails();

    public static ProblemDetails From(List<Error> errors)
    {
        if (
            errors.Any(e =>
                Fields.ContainsKey(e.Code) || e.Code == "Flights.PassengerDetailsInvalid"
            )
        )
        {
            var problem = Invalid();
            problem.Extensions["fieldErrors"] = errors
                .Where(e => Fields.ContainsKey(e.Code))
                .Select(e => new { field = Fields[e.Code], code = e.Code })
                .Distinct()
                .Take(7)
                .ToArray();
            return problem;
        }
        // Only fixed codes/descriptions leave this boundary; metadata and provider exception text never do.
        var safe = errors.FirstOrDefault().Code switch
        {
            "Flights.TravelerNotFound" => SavedTravelerErrors.NotFound,
            "Flights.TravelerPreconditionFailed" => SavedTravelerErrors.PreconditionFailed,
            "Flights.TravelerPageInvalid" => SavedTravelerErrors.PageInvalid,
            "Flights.TravelerIdInvalid" => SavedTravelerErrors.IdInvalid,
            "Flights.TravelerIdentityInvalid" => SavedTravelerErrors.IdentityInvalid,
            "Flights.TravelerPreconditionRequired" => SavedTravelerErrors.PreconditionRequired,
            "Flights.TravelerPreconditionInvalid" => SavedTravelerErrors.PreconditionInvalid,
            "Flights.PiiProtectionUnavailable" => PiiProtectionErrors.Unavailable,
            "Flights.PiiPayloadUnavailable" => PiiProtectionErrors.PayloadUnavailable,
            _ => SavedTravelerErrors.StorageUnavailable,
        };
        return new List<Error> { safe }.ToProblemDetails();
    }
}
