using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core.Itineraries;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class ItineraryAvailabilityEndpoint
{
    [WolverineGet("/api/trips/itinerary-availability")]
    [Authorize]
    public static async Task<IResult> Get(
        HttpContext context,
        [FromServices] IItineraryFactSource source,
        [FromServices] TimeProvider clock,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        try
        {
            var facts = await source.ReadAsync(ct);
            if (facts.IsError)
                return TripsProblems.Problem("Trips.ItineraryUnavailable", 503);
            var value = facts.Value;
            var now = clock.GetUtcNow();
            if (
                value.Destination.DestinationId != "moscow"
                || now < value.BuildAsOfUtc
                || now >= value.ReleaseValidUntil
                || now >= value.ModelSource.ValidUntil
            )
                return TripsProblems.Problem("Trips.ItineraryUnavailable", 503);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    candidateVersion = 1,
                    status = "ready",
                    destinationId = "moscow",
                    datasetId = value.DatasetId,
                    datasetVersion = value.Version,
                    datasetHash = value.DatasetHash,
                    policyVersion = ItineraryPolicy.PolicyVersion,
                    dataMode = value.DataMode,
                    humanQuality = value.HumanQuality,
                    releaseValidUntil = ItineraryCandidateResponseEncoder.Utc(
                        value.ReleaseValidUntil
                    ),
                }
            );
            // The metadata is best effort after this last check, not a lease over other replicas.
            if (bytes.Length > 2048 || clock.GetUtcNow() >= value.ReleaseValidUntil)
                return TripsProblems.Problem("Trips.ItineraryUnavailable", 503);
            return new FrozenItineraryJson(bytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return TripsProblems.Problem("Trips.ItineraryUnavailable", 503);
        }
    }
}
