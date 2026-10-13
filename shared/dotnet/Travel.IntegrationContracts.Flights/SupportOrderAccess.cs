using ErrorOr;

namespace Travel.IntegrationContracts.Flights;

/// <summary>An authoritative, read-only ownership proof at the recorded instant.</summary>
public sealed record FlightOrderOwnershipProof(
    Guid OrderId,
    Guid OwnerId,
    long StreamVersion,
    DateTimeOffset VerifiedAt
);

public interface IFlightsSupportOrderAccess
{
    Task<ErrorOr<FlightOrderOwnershipProof>> VerifyOwnershipAsync(
        Guid ownerId,
        Guid orderId,
        CancellationToken cancellationToken = default
    );
}
