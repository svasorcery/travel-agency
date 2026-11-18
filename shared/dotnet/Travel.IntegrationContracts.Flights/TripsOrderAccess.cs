using ErrorOr;

namespace Travel.IntegrationContracts.Flights.Trips;

public enum FlightOrderBookingStatus
{
    Held,
    Confirmed,
    Ticketed,
    Cancelled,
    Refunded,
}

public sealed record FlightOrderLeg(
    string Origin,
    string Destination,
    DateTimeOffset DepartAt,
    DateTimeOffset ArriveAt
);

/// <summary>A null route means the complete projected itinerary is unavailable.</summary>
public sealed record FlightOrderCandidate(
    Guid OrderId,
    FlightOrderBookingStatus BookingStatus,
    int PassengerCount,
    IReadOnlyList<FlightOrderLeg>? Route
);

public sealed record FlightOrderCandidatePage(
    IReadOnlyList<FlightOrderCandidate> Items,
    int? NextOffset,
    bool PageBoundReached
);

/// <summary>Owner-bound source observation. A null route preserves unknown itinerary facts.</summary>
public sealed record FlightOrderObservation(
    Guid OrderId,
    Guid OwnerId,
    long StreamVersion,
    DateTimeOffset ObservedAt,
    FlightOrderBookingStatus BookingStatus,
    bool ReviewRequired,
    int PassengerCount,
    IReadOnlyList<FlightOrderLeg>? Route
);

public interface IFlightsTripsOrderAccess
{
    Task<ErrorOr<FlightOrderCandidatePage>> ListCandidatesAsync(
        Guid ownerId,
        int offset,
        CancellationToken cancellationToken = default
    );

    Task<ErrorOr<FlightOrderObservation>> ReadAsync(
        Guid ownerId,
        Guid orderId,
        CancellationToken cancellationToken = default
    );
}
