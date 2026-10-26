using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.Itineraries;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class ItineraryCandidateEndpoint
{
    [WolverinePost("/api/trips/{tripId:guid}/itinerary-candidate")]
    [Authorize]
    public static async Task<IResult> Post(
        Guid tripId,
        HttpContext context,
        [FromHeader(Name = TripsItineraryProtocol.Header)] string? itineraryVersion,
        [FromServices] ItineraryCandidateService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var protocol = TripsItineraryProtocol.Version(context.Request);
        if (protocol.IsError)
            return TripsEndpoint.Failure(protocol.FirstError);
        var revision = TripsEndpoint.Revision(context);
        if (revision.IsError)
            return TripsEndpoint.Failure(revision.FirstError);
        var response = await service.GenerateAsync(
            owner.Value,
            tripId,
            revision.Value,
            ct,
            protocol.Value
        );
        if (response.IsError)
        {
            if (response.FirstError.Code == "Trips.ItineraryBusy")
                context.Response.Headers.RetryAfter = "1";
            return TripsEndpoint.Failure(response.FirstError);
        }
        return new FrozenItineraryJson(response.Value.Utf8Json);
    }
}

public sealed class FrozenItineraryJson(ReadOnlyMemory<byte> bytes) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }
}
