using ErrorOr;

namespace Travel.IntegrationContracts.Hotels.Trips;

public enum HotelTripReservationState
{
    Confirmed,
    Cancelled,
    ReviewRequired,
    Ineligible,
    Unavailable,
}

public sealed record HotelTripReservationObservation(
    HotelTripReservationState State,
    string? PropertyName = null,
    DateOnly? CheckIn = null,
    DateOnly? CheckOut = null
)
{
    public override string ToString() => nameof(HotelTripReservationObservation);
}

public interface IHotelsTripsReservationAccess
{
    bool IsAvailable { get; }

    Task<ErrorOr<HotelTripReservationObservation>> ReadAsync(
        Guid ownerId,
        Guid reservationId,
        CancellationToken ct = default
    );
}
