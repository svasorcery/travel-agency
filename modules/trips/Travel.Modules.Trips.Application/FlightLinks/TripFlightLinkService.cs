using ErrorOr;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.FlightLinks;

public sealed class TripFlightLinkService(
    ITripFlightLinkStore store,
    ITripFlightLinkCodec codec,
    ITripFlightLinkProtector protector,
    Func<TripFlightLinkSource> flights,
    TimeProvider time
)
{
    public async Task<ErrorOr<TripFlightLinks>> GetAsync(
        OwnerIdentity? owner,
        Guid tripId,
        CancellationToken ct
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        if (tripId == Guid.Empty)
            return TripErrors.NotFound;
        ct.ThrowIfCancellationRequested();
        var read = await store.LoadAsync(owner, tripId, ct);
        ct.ThrowIfCancellationRequested();
        if (read.IsError)
            return read.Errors;
        if (read.Value is not { } row)
        {
            var ready = protector.IsReady;
            ct.ThrowIfCancellationRequested();
            return ready ? TripFlightLinks.Restore(tripId, 0, []) : TripErrors.Unavailable;
        }
        if (row.Binding.TripId != tripId || row.Binding.Owner != owner || row.Binding.Revision <= 0)
            return TripErrors.Unavailable;
        var plain = protector.Unprotect(row.ProtectedPayload, row.Binding);
        ct.ThrowIfCancellationRequested();
        if (plain.IsError)
            return TripErrors.Unavailable;
        var result = codec.Deserialize(plain.Value, row.Binding);
        ct.ThrowIfCancellationRequested();
        return
            result.IsError
            || result.Value.TripId != tripId
            || result.Value.Revision != row.Binding.Revision
            ? TripErrors.Unavailable
            : result.Value;
    }

    public async Task<ErrorOr<TripFlightLinks>> PatchAsync(
        OwnerIdentity? owner,
        Guid tripId,
        long expectedRevision,
        bool add,
        Guid orderId,
        CancellationToken ct
    )
    {
        if (orderId == Guid.Empty || expectedRevision < 0)
            return TripErrors.InvalidInput;
        var loaded = await GetAsync(owner, tripId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        var current = loaded.Value;
        if (current.Revision != expectedRevision)
            return TripErrors.RevisionMismatch;
        var change = current.Change(add, orderId, time.GetUtcNow());
        if (change.IsError)
            return change.Errors;
        TripFlightLinkRecord? replacement = null;
        if (change.Value.Revision != current.Revision)
        {
            if (add)
            {
                var proof = await flights().ObserveAsync(owner!, orderId, time, ct);
                if (proof.IsError)
                    return proof.Errors;
            }
            var plain = codec.Serialize(change.Value);
            if (plain.IsError)
                return TripErrors.Unavailable;
            var binding = new TripFlightLinkBinding(owner!, tripId, change.Value.Revision);
            var envelope = protector.Protect(plain.Value, binding);
            if (envelope.IsError)
                return TripErrors.Unavailable;
            replacement = new(binding, envelope.Value);
        }
        ct.ThrowIfCancellationRequested();
        var saved = await store.CommitAsync(new(owner!, tripId, expectedRevision), replacement, ct);
        ct.ThrowIfCancellationRequested();
        return saved.IsError ? saved.Errors : change.Value;
    }

    public async Task<ErrorOr<TripFlightStatusPage>> StatusAsync(
        OwnerIdentity? owner,
        Guid tripId,
        CancellationToken ct
    )
    {
        var loaded = await GetAsync(owner, tripId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        var items = new List<TripFlightStatusItem>();
        foreach (var link in loaded.Value.Links)
        {
            var seen = await flights().ObserveAsync(owner!, link.OrderId, time, ct);
            items.Add(
                seen.IsError
                    ? new(
                        link.OrderId,
                        seen.FirstError.Type == ErrorType.NotFound ? "notFound" : "unavailable",
                        null
                    )
                    : new(link.OrderId, "available", seen.Value)
            );
        }
        ct.ThrowIfCancellationRequested();
        return new TripFlightStatusPage(tripId, loaded.Value.Revision, items.AsReadOnly());
    }

    public async Task<ErrorOr<TripFlightCandidatePage>> CandidatesAsync(
        OwnerIdentity? owner,
        Guid tripId,
        int offset,
        CancellationToken ct
    )
    {
        if (offset is < 0 or > 10000)
            return TripErrors.InvalidInput;
        var loaded = await GetAsync(owner, tripId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        return await flights().CandidatesAsync(owner!, tripId, offset, ct);
    }
}
