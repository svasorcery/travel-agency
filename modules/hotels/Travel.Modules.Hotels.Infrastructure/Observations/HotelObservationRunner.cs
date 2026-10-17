using System.Text.Json;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Infrastructure.Observations;

public sealed class HotelObservationRunner(
    IHotelReservationStore store,
    HotelReservationService reservations,
    IEnumerable<IHotelBookingProvider> providers,
    IHotelCapacity capacity,
    TimeProvider time
)
{
    public async Task RunAsync(HotelWork work, CancellationToken ct)
    {
        var loaded = await store.LoadAsync(work.ReservationId, null, ct);
        if (loaded.IsError)
            throw new InvalidOperationException("Hotels observation store unavailable.");
        var row = loaded.Value;
        if (row?.AttemptId is null)
            return;
        var now = time.GetUtcNow();
        if (work.Escalation)
        {
            var before = JsonSerializer.Serialize(row.State);
            row.State.Escalate(now);
            if (before != JsonSerializer.Serialize(row.State))
            {
                var result = await store.SaveAsync(row, row.Version, [], ct);
                if (result.IsError || !result.Value)
                    throw new InvalidOperationException("Hotels escalation store unavailable.");
            }
            return;
        }
        if (
            row.State.BoundBookingId is null
            || row.State.ReadUntil <= now
            || row.State.ReadsClaimed >= row.State.ReadAllowance
            || row.State.NextReadAt > now
            || row.State.Outcome is ReservationOutcome.Confirmed or ReservationOutcome.Cancelled
        )
            return;
        if (
            providers.SingleOrDefault(p => p.Key == row.Provider)
            is not IHotelReservationReader reader
        )
        {
            if (row.State.Review.Add(ReviewReason.ObservationUnavailable))
            {
                var unavailable = await store.SaveAsync(row, row.Version, [], ct);
                if (unavailable.IsError || !unavailable.Value)
                    throw new InvalidOperationException("Hotels observation store unavailable.");
            }
            return;
        }
        using var permit = capacity.TryAcquire(row.Provider, recovery: true);
        if (permit is null)
        {
            var due = now.AddSeconds(30);
            if (due < row.State.ReadUntil)
            {
                // One durable successor under the same finite window, without spending a read slot.
                row.State.NextReadAt = due;
                var denied = await store.SaveAsync(row, row.Version, [new(row.Id, false, due)], ct);
                if (denied.IsError)
                    throw new InvalidOperationException("Hotels observation store unavailable.");
            }
            return;
        }
        if (!row.State.ClaimRead(now))
            return;
        var next =
            row.State.ReadsClaimed < row.State.ReadAllowance
            && row.State.NextReadAt < row.State.ReadUntil
                ? new[] { new HotelWork(row.Id, false, row.State.NextReadAt!.Value) }
                : [];
        var claimed = await store.SaveAsync(row, row.Version, next, ct);
        if (claimed.IsError)
            throw new InvalidOperationException("Hotels read claim unavailable.");
        if (!claimed.Value)
            return;
        var observation = await reader.ReadAsync(row.State.BoundBookingId, ct);
        var applied = await reservations.ApplyAsync(row.Id, observation, false, ct);
        if (applied.IsError)
            throw new InvalidOperationException("Hotels observation could not be saved.");
    }
}

public static class HotelWorkHandler
{
    public static Task Handle(
        HotelWork work,
        HotelObservationRunner runner,
        CancellationToken ct
    ) => runner.RunAsync(work, ct);
}
