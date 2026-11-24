using ErrorOr;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.HotelLinks;

public sealed record TripHotelLinkBinding(OwnerIdentity Owner, Guid TripId, long Revision);

public sealed record TripHotelLinkRecord(TripHotelLinkBinding Binding, string ProtectedPayload)
{
    public override string ToString() => nameof(TripHotelLinkRecord);
}

public interface ITripHotelLinkStore
{
    Task<bool> IsReadyAsync(CancellationToken ct);

    // A null record represents a virtual revision zero for a proven owned trip.
    Task<ErrorOr<TripHotelLinkRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    );

    // A null replacement checks the current owner and revision without writing.
    Task<ErrorOr<Success>> CommitAsync(
        TripHotelLinkBinding expected,
        TripHotelLinkRecord? replacement,
        CancellationToken ct
    );
}

public interface ITripHotelLinkProtector
{
    bool IsReady { get; }
    ErrorOr<string> Protect(Guid? reservationId, TripHotelLinkBinding binding);
    ErrorOr<Guid?> Unprotect(string envelope, TripHotelLinkBinding binding);
}
