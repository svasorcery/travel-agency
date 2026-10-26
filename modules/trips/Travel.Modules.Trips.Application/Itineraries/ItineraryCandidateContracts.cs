using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Application.Itineraries;

public abstract record ItineraryCandidateResult(long BaseRevision)
{
    public int CandidateVersion { get; init; } = 1;
}

/// <summary>A scope decision made before consulting any itinerary facts.</summary>
public sealed record ScopeUnsupportedCandidateResult(
    long BaseRevision,
    DestinationSelection Destination,
    string Reason,
    string PolicyVersion,
    DateTimeOffset AsOfUtc
) : ItineraryCandidateResult(BaseRevision);

/// <summary>Facts are ephemeral DTO rendering context, never a whole-package wire payload.</summary>
public sealed record ComputedCandidateResult(
    long BaseRevision,
    ItineraryOutcome Outcome,
    ValidatedItineraryFacts Facts
) : ItineraryCandidateResult(BaseRevision);

/// <summary>Exact complete JSON copied from the encoder before the final owner read.</summary>
public sealed class PreparedCandidateResponse
{
    public ReadOnlyMemory<byte> Utf8Json { get; }

    internal PreparedCandidateResponse(ReadOnlyMemory<byte> utf8Json) =>
        Utf8Json = utf8Json.ToArray();
}
