using ErrorOr;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.HotelLinks;

public sealed record TripHotelLinkMetadata(bool HasLink, long Revision);

public sealed record TripHotelLinkStatus(
    long Revision,
    string State,
    Guid? ReservationId = null,
    string? PropertyName = null,
    DateOnly? CheckIn = null,
    DateOnly? CheckOut = null
)
{
    public override string ToString() => nameof(TripHotelLinkStatus);
}

public sealed class TripHotelLinkService(
    ITripHotelLinkStore store,
    ITripHotelLinkProtector protector,
    Func<IHotelsTripsReservationAccess?> getHotels
)
{
    private static readonly Error Occupied = Error.Conflict(
        "Trips.HotelLinkOccupied",
        "Remove the existing hotel link before attaching another order."
    );
    private static readonly Error OrderIneligible = Error.Conflict(
        "Trips.HotelLinkOrderIneligible",
        "Only an eligible confirmed hotel order can be linked."
    );

    public Task<bool> IsReadyAsync(CancellationToken ct) =>
        protector.IsReady ? store.IsReadyAsync(ct) : Task.FromResult(false);

    public bool IsHotelsEnabled
    {
        get
        {
            try
            {
                return getHotels()?.IsAvailable == true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public async Task<ErrorOr<TripHotelLinkMetadata>> MetadataAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    )
    {
        var current = await LoadAsync(owner, tripId, ct);
        return current.IsError ? current.Errors : current.Value.Metadata;
    }

    public async Task<ErrorOr<TripHotelLinkStatus>> StatusAsync(
        OwnerIdentity owner,
        Guid tripId,
        bool canReadHotels,
        CancellationToken ct
    )
    {
        var loaded = await LoadAsync(owner, tripId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        var link = loaded.Value;
        if (link.ReservationId is not { } id)
            return new TripHotelLinkStatus(link.Revision, "empty");
        if (!canReadHotels)
            return new TripHotelLinkStatus(link.Revision, "unavailable");

        var seen = await ObserveAsync(owner, id, ct);
        ct.ThrowIfCancellationRequested();
        if (seen.IsError)
            return new TripHotelLinkStatus(link.Revision, "unavailable");
        var facts = seen.Value;
        return facts.State switch
        {
            HotelTripReservationState.Confirmed when IsUsable(facts) => new TripHotelLinkStatus(
                link.Revision,
                "confirmed",
                id,
                facts.PropertyName,
                facts.CheckIn,
                facts.CheckOut
            ),
            HotelTripReservationState.Cancelled => new TripHotelLinkStatus(
                link.Revision,
                "cancelled"
            ),
            HotelTripReservationState.ReviewRequired => new TripHotelLinkStatus(
                link.Revision,
                "reviewRequired"
            ),
            _ => new TripHotelLinkStatus(link.Revision, "unavailable"),
        };
    }

    public async Task<ErrorOr<TripHotelLinkMetadata>> PatchAsync(
        OwnerIdentity owner,
        Guid tripId,
        long expectedRevision,
        Guid? reservationId,
        bool canReadHotels,
        CancellationToken ct
    )
    {
        if (expectedRevision < 0 || reservationId == Guid.Empty)
            return TripErrors.InvalidInput;
        var loaded = await LoadAsync(owner, tripId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        var current = loaded.Value;
        if (current.Revision != expectedRevision)
            return TripErrors.RevisionMismatch;

        if (reservationId is { } id)
        {
            if (!canReadHotels)
                return TripErrors.Forbidden;
            var proof = await ObserveAsync(owner, id, ct);
            ct.ThrowIfCancellationRequested();
            if (proof.IsError)
                return proof.Errors;
            if (proof.Value.State == HotelTripReservationState.Unavailable)
                return TripErrors.Unavailable;
            if (!IsUsable(proof.Value))
                return OrderIneligible;
            if (current.ReservationId is { } linked && linked != id)
                return Occupied;
        }

        var binding = new TripHotelLinkBinding(owner, tripId, expectedRevision);
        TripHotelLinkRecord? replacement = null;
        if (reservationId != current.ReservationId)
        {
            if (binding.Revision == long.MaxValue)
                return TripErrors.RevisionExhausted;
            var advanced = binding with { Revision = binding.Revision + 1 };
            var encrypted = protector.Protect(reservationId, advanced);
            if (encrypted.IsError)
                return TripErrors.Unavailable;
            replacement = new(advanced, encrypted.Value);
        }
        ct.ThrowIfCancellationRequested();
        var saved = await store.CommitAsync(binding, replacement, ct);
        return saved.IsError
            ? saved.Errors
            : new TripHotelLinkMetadata(
                reservationId.HasValue,
                replacement?.Binding.Revision ?? current.Revision
            );
    }

    private async Task<ErrorOr<StoredLink>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    )
    {
        if (tripId == Guid.Empty)
            return TripErrors.NotFound;
        ct.ThrowIfCancellationRequested();
        var stored = await store.LoadAsync(owner, tripId, ct);
        ct.ThrowIfCancellationRequested();
        if (stored.IsError)
            return stored.Errors;
        if (!await IsReadyAsync(ct))
            return TripErrors.Unavailable;
        if (stored.Value is not { } row)
            return new StoredLink(0, null);
        if (row.Binding.Owner != owner || row.Binding.TripId != tripId || row.Binding.Revision <= 0)
            return TripErrors.Unavailable;
        var id = protector.Unprotect(row.ProtectedPayload, row.Binding);
        return id.IsError || id.Value == Guid.Empty
            ? TripErrors.Unavailable
            : new StoredLink(row.Binding.Revision, id.Value);
    }

    private async Task<ErrorOr<HotelTripReservationObservation>> ObserveAsync(
        OwnerIdentity owner,
        Guid reservationId,
        CancellationToken ct
    )
    {
        try
        {
            if (getHotels() is not { IsAvailable: true } hotels)
                return TripErrors.Unavailable;
            var result = await hotels.ReadAsync(owner.Subject, reservationId, ct);
            if (!result.IsError)
                return result.Value;
            return result.FirstError.Type == ErrorType.NotFound
                ? TripErrors.NotFound
                : TripErrors.Unavailable;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return TripErrors.Unavailable;
        }
    }

    private static bool IsUsable(HotelTripReservationObservation facts) =>
        facts.State == HotelTripReservationState.Confirmed
        && facts.PropertyName is { Length: <= 4096 } name
        && name.Any(character => !char.IsWhiteSpace(character) && character != '\uFEFF')
        && !name.Any(char.IsControl)
        && facts.CheckIn is { } checkIn
        && facts.CheckOut is { } checkOut
        && checkOut > checkIn;

    private sealed record StoredLink(long Revision, Guid? ReservationId)
    {
        public TripHotelLinkMetadata Metadata => new(ReservationId.HasValue, Revision);

        public override string ToString() => nameof(StoredLink);
    }
}
