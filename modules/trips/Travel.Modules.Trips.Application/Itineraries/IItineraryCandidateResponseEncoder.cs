using ErrorOr;

namespace Travel.Modules.Trips.Application.Itineraries;

/// <summary>API-owned strict DTO projection. Implementations must cooperate with cancellation.</summary>
public interface IItineraryCandidateResponseEncoder
{
    Task<ErrorOr<ReadOnlyMemory<byte>>> EncodeAsync(
        ItineraryCandidateResult result,
        CancellationToken cancellationToken = default
    );
}
