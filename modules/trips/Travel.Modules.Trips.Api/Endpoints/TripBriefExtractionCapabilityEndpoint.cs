using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Composition;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class TripBriefExtractionCapabilityEndpoint
{
    [WolverineGet("/api/trips/brief-extraction-capability")]
    [Authorize]
    public static IResult Get(
        HttpContext context,
        [FromServices] TripsBriefExtractionFeature feature
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var configured = feature.Configured;
        return Results.Json(
            new
            {
                candidateVersion = 1,
                status = configured ? "configured" : "unconfigured",
                processorId = configured ? feature.ProcessorId : null,
                modelId = configured ? feature.ModelId : null,
                processingPolicyVersion = configured ? feature.ProcessingPolicyVersion : null,
            }
        );
    }
}
