using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Travel.Modules.Trips.Api.Contracts;

public static class TripsProblems
{
    public static IResult Error(Error error) => Problem(error.Code, Status(error.Code));

    public static IResult Problem(string code, int status) =>
        Results.Problem(
            new ProblemDetails
            {
                Status = status,
                Title = status switch
                {
                    400 => "Invalid Trips request.",
                    401 => "Authentication required.",
                    403 => "Trips access denied.",
                    404 => "Trip plan not found.",
                    409 => "Trip plan conflict.",
                    412 => "Trip plan revision changed.",
                    428 => "A trip plan revision is required.",
                    _ => "Trips is unavailable.",
                },
                Type = "about:blank",
                Extensions = { ["code"] = code },
            }
        );

    private static int Status(string code) =>
        code switch
        {
            "Trips.Forbidden" => 403,
            "Trips.NotFound" => 404,
            "Trips.RevisionMismatch" => 412,
            "Trips.CapacityExceeded"
            or "Trips.DateRangeWouldDropBlocks"
            or "Trips.BriefChangeRequiresEmptyPlan"
            or "Trips.RevisionExhausted"
            or "Trips.ItineraryRequiresEmptyPlan" => 409,
            "Trips.Unavailable" or "Trips.ItineraryUnavailable" => 503,
            "Trips.ItineraryBusy" => 429,
            "Trips.ItineraryDeadline" => 504,
            _ => 400,
        };
}
