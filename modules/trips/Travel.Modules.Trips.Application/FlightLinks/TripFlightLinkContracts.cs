using ErrorOr;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.FlightLinks;

public sealed record TripFlightLinkBinding(OwnerIdentity Owner, Guid TripId, long Revision);

public sealed record TripFlightLinkRecord(TripFlightLinkBinding Binding, string ProtectedPayload)
{
    public override string ToString() => nameof(TripFlightLinkRecord);
}

public interface ITripFlightLinkStore
{
    // Null is a virtual empty collection of a proven owned parent. A missing parent returns NotFound.
    Task<ErrorOr<TripFlightLinkRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    );

    // Null replacement performs a checked no-op. Commit must check both parent ownership and current revision.
    Task<ErrorOr<Success>> CommitAsync(
        TripFlightLinkBinding expected,
        TripFlightLinkRecord? replacement,
        CancellationToken ct
    );
}

public interface ITripFlightLinkCodec
{
    ErrorOr<string> Serialize(TripFlightLinks links);
    ErrorOr<TripFlightLinks> Deserialize(string plaintext, TripFlightLinkBinding binding);
}

public interface ITripFlightLinkProtector
{
    const int MaximumBytes = 8192;
    bool IsReady { get; }
    ErrorOr<string> Protect(string plaintext, TripFlightLinkBinding binding);
    ErrorOr<string> Unprotect(string envelope, TripFlightLinkBinding binding);
}

public sealed record TripFlightLeg(
    string Origin,
    string Destination,
    DateTimeOffset DepartAt,
    DateTimeOffset ArriveAt
);

public sealed record TripFlightCandidate(
    Guid OrderId,
    string BookingStatus,
    int PassengerCount,
    IReadOnlyList<TripFlightLeg>? Route
);

public sealed record TripFlightCandidatePage(
    Guid TripId,
    IReadOnlyList<TripFlightCandidate> Items,
    int? NextOffset,
    bool LimitReached
);

public sealed record TripFlightObservation(
    string BookingStatus,
    int PassengerCount,
    IReadOnlyList<TripFlightLeg>? Route,
    bool ReviewRequired,
    DateTimeOffset ObservedAt,
    long StreamVersion
);

public sealed record TripFlightStatusItem(
    Guid OrderId,
    string State,
    TripFlightObservation? Observation
);

public sealed record TripFlightStatusPage(
    Guid TripId,
    long LinksRevision,
    IReadOnlyList<TripFlightStatusItem> Items
);
