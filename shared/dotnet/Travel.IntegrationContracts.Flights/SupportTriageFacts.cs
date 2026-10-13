using ErrorOr;

namespace Travel.IntegrationContracts.Flights;

public enum FlightSupportJourneyState
{
    Unknown,
    Active,
    Closed,
}

/// <summary>Owner-bound, source-observed journey metadata; never payment, bank or supplier authority.</summary>
public sealed record FlightSupportTriageFacts(
    Guid OrderId,
    Guid OwnerId,
    long StreamVersion,
    DateTimeOffset ObservedAt,
    FlightSupportJourneyState Journey,
    DateTimeOffset? NextScheduledDepartureAt
);

public interface IFlightsSupportTriageContext
{
    Task<ErrorOr<FlightSupportTriageFacts>> ReadAsync(
        Guid ownerId,
        Guid orderId,
        CancellationToken cancellationToken = default
    );
}
