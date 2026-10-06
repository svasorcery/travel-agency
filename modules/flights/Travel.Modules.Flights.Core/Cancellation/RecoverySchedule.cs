using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Cancellation;

public enum RecoveryReservationKind
{
    NoOp = 0,
    Read = 1,
    Skipped = 2,
    NotDue = 3,
    Manual = 4,
}

public sealed record RecoveryReservation(
    RecoveryReservationKind Kind,
    RecoverySchedule UpdatedSchedule,
    EquatableArray<int> ConsumedSlots
);

public sealed record RecoverySchedule
{
    public Guid Epoch { get; }
    public DateTimeOffset ClaimedAt { get; }
    public int ConsumedMask { get; }
    public DateTimeOffset? ReadWindowStartedAt { get; }
    public DateTimeOffset? ReadWindowUntil { get; }
    public DateTimeOffset Deadline => ClaimedAt.AddSeconds(310);

    [JsonConstructor]
    private RecoverySchedule(
        Guid epoch,
        DateTimeOffset claimedAt,
        int consumedMask,
        DateTimeOffset? readWindowStartedAt,
        DateTimeOffset? readWindowUntil
    )
    {
        Epoch = epoch;
        ClaimedAt = claimedAt;
        ConsumedMask = consumedMask;
        ReadWindowStartedAt = readWindowStartedAt;
        ReadWindowUntil = readWindowUntil;
    }

    public static ErrorOr<RecoverySchedule> Create(Guid epoch, DateTimeOffset claimedAt)
    {
        if (epoch == Guid.Empty)
            return Error.Validation(
                "Flights.RecoveryScheduleInvalid",
                "Recovery identity is required."
            );
        try
        {
            _ = claimedAt.AddSeconds(310);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error.Validation(
                "Flights.RecoveryScheduleInvalid",
                "Recovery deadline is out of range."
            );
        }
        return new RecoverySchedule(epoch, claimedAt, 0, null, null);
    }

    public RecoveryReservation Reserve(int slot, DateTimeOffset now)
    {
        if (slot is < 0 or > 3 || (ConsumedMask & (1 << slot)) != 0)
            return Unchanged(RecoveryReservationKind.NoOp);
        if (now >= Deadline)
            return Unchanged(RecoveryReservationKind.Manual);

        int[] dueSeconds = [2, 10, 60, 300];
        var latestDue = -1;
        for (var index = 0; index < dueSeconds.Length; index++)
            if (ClaimedAt.AddSeconds(dueSeconds[index]) <= now)
                latestDue = index;
        if (slot > latestDue)
            return Unchanged(RecoveryReservationKind.NotDue);
        // Only the latest due envelope consolidates overdue slots. Older envelopes do not GET.
        if (slot < latestDue)
            return Unchanged(RecoveryReservationKind.NoOp);

        var consumed = Enumerable
            .Range(0, latestDue + 1)
            .Where(index => (ConsumedMask & (1 << index)) == 0)
            .ToArray();
        var mask = ConsumedMask | ((1 << (latestDue + 1)) - 1);
        if (ReadWindowUntil > now)
            return new RecoveryReservation(
                RecoveryReservationKind.Skipped,
                new RecoverySchedule(Epoch, ClaimedAt, mask, ReadWindowStartedAt, ReadWindowUntil),
                new(consumed)
            );

        var until = Deadline - now < TimeSpan.FromSeconds(10) ? Deadline : now.AddSeconds(10);
        return new RecoveryReservation(
            RecoveryReservationKind.Read,
            new RecoverySchedule(Epoch, ClaimedAt, mask, now, until),
            new(consumed)
        );
    }

    public RecoverySchedule CompleteWindow(DateTimeOffset startedAt) =>
        ReadWindowStartedAt == startedAt
            ? new RecoverySchedule(Epoch, ClaimedAt, ConsumedMask, null, null)
            : this;

    internal RecoverySchedule OpenSharedWindow(DateTimeOffset now) =>
        new(Epoch, ClaimedAt, ConsumedMask, now, now.AddSeconds(10));

    private RecoveryReservation Unchanged(RecoveryReservationKind kind) => new(kind, this, new([]));
}
