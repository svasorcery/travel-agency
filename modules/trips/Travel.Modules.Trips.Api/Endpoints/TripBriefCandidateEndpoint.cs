using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.BriefExtraction;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class TripBriefCandidateEndpoint
{
    [WolverinePost("/api/trips/brief-candidate")]
    [Authorize]
    public static async Task<IResult> Post(
        HttpContext context,
        [FromServices] TripBriefExtractionService service,
        [FromServices] TimeProvider clock,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        if (context.Items[TripBriefExtractionDtos.InputKey] is not TripBriefExtractionInput input)
            return TripBriefExtractionDtos.Failure(TripBriefErrors.InvalidInput);
        var candidate = await service.ExtractAsync(input, ct);
        if (candidate.IsError)
        {
            if (candidate.FirstError.Code == "Trips.BriefBusy")
                context.Response.Headers.RetryAfter = "60";
            return TripBriefExtractionDtos.Failure(candidate.FirstError);
        }
        var encoded = TripBriefExtractionDtos.Encode(candidate.Value);
        if (clock.GetUtcNow() >= input.Deadline)
            return TripBriefExtractionDtos.Failure(TripBriefErrors.Deadline);
        return encoded.IsError
            ? TripBriefExtractionDtos.Failure(encoded.FirstError)
            : new FrozenItineraryJson(encoded.Value);
    }
}
