using System.Text;
using ErrorOr;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Application.Trips;

public sealed class HotelsTripsReservationAccess(
    IHotelReservationStore store,
    TimeProvider time,
    bool enabled
) : IHotelsTripsReservationAccess
{
    private const int PropertyNameLimit = 4096;

    public bool IsAvailable => enabled;

    public async Task<ErrorOr<HotelTripReservationObservation>> ReadAsync(
        Guid ownerId,
        Guid reservationId,
        CancellationToken ct = default
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!IsAvailable)
            return new HotelTripReservationObservation(HotelTripReservationState.Unavailable);
        if (ownerId == Guid.Empty || reservationId == Guid.Empty)
            return HotelErrors.Missing;

        ErrorOr<HotelReservation?> loaded;
        try
        {
            loaded = await store.LoadAsync(reservationId, ownerId, ct);
        }
        catch (ArgumentException)
        {
            // A stored value can fail its domain constructor while the row is being decoded.
            return new HotelTripReservationObservation(HotelTripReservationState.Unavailable);
        }

        if (loaded.IsError)
            return new HotelTripReservationObservation(HotelTripReservationState.Unavailable);
        if (
            loaded.Value is not { } row
            || row.Id != reservationId
            || row.OwnerId != ownerId
            || row.PrivatePurgeAt <= time.GetUtcNow()
        )
            return HotelErrors.Missing;

        var state = row.State;
        if (state?.Review is null)
            return new HotelTripReservationObservation(HotelTripReservationState.Unavailable);
        if (state.Outcome == ReservationOutcome.Cancelled)
            return new HotelTripReservationObservation(HotelTripReservationState.Cancelled);
        if (state.Review.Count > 0)
            return new HotelTripReservationObservation(HotelTripReservationState.ReviewRequired);

        var actual = row.Terms is not null ? state.Facts?.Actual : null;
        if (
            state.Outcome != ReservationOutcome.Confirmed
            || !state.Active
            || actual?.Property?.Name is not { Length: <= PropertyNameLimit } name
            || !IsUsablePropertyName(name)
            || actual.Dates is not { } dates
            || dates.CheckIn == default
            || dates.CheckOut <= dates.CheckIn
        )
            return new HotelTripReservationObservation(HotelTripReservationState.Ineligible);

        return new HotelTripReservationObservation(
            HotelTripReservationState.Confirmed,
            name,
            dates.CheckIn,
            dates.CheckOut
        );
    }

    private static bool IsUsablePropertyName(string name)
    {
        var hasText = false;
        for (var index = 0; index < name.Length; )
        {
            if (!Rune.TryGetRuneAt(name, index, out var rune) || Rune.IsControl(rune))
                return false;
            if (!Rune.IsWhiteSpace(rune) && rune.Value != '\uFEFF')
                hasText = true;
            index += rune.Utf16SequenceLength;
        }
        return hasText;
    }
}
