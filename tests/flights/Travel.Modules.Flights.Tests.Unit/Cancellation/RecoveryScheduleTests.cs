using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class RecoveryScheduleTests
{
    private static readonly DateTimeOffset At = CancellationTestData.Now;

    [Fact]
    public void Invalid_epoch_or_time_cannot_create_recovery_work()
    {
        RecoverySchedule.Create(Guid.Empty, At).IsError.ShouldBeTrue();
        RecoverySchedule.Create(Guid.NewGuid(), DateTimeOffset.MaxValue).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Before_due_time_does_not_consume_a_slot_or_open_a_read()
    {
        var schedule = RecoverySchedule.Create(Guid.NewGuid(), At).Value;
        var reservation = schedule.Reserve(0, At.AddSeconds(1));
        reservation.Kind.ShouldBe(RecoveryReservationKind.NotDue);
        reservation.UpdatedSchedule.ConsumedMask.ShouldBe(0);
        reservation.UpdatedSchedule.ReadWindowUntil.ShouldBeNull();
    }

    [Fact]
    public void Duplicate_slot_does_not_get_a_second_read()
    {
        var schedule = RecoverySchedule.Create(Guid.NewGuid(), At).Value;
        var first = schedule.Reserve(0, At.AddSeconds(2));
        first.Kind.ShouldBe(RecoveryReservationKind.Read);
        first.UpdatedSchedule.ConsumedMask.ShouldBe(1);
        first
            .UpdatedSchedule.Reserve(0, At.AddSeconds(3))
            .Kind.ShouldBe(RecoveryReservationKind.NoOp);
    }

    [Fact]
    public void A_busy_window_consumes_the_next_due_slot_without_another_read()
    {
        var first = RecoverySchedule.Create(Guid.NewGuid(), At).Value.Reserve(0, At.AddSeconds(2));
        var blocked = first.UpdatedSchedule.Reserve(1, At.AddSeconds(10));
        blocked.Kind.ShouldBe(RecoveryReservationKind.Skipped);
        blocked.UpdatedSchedule.ConsumedMask.ShouldBe(3);
        blocked
            .UpdatedSchedule.Reserve(1, At.AddSeconds(12))
            .Kind.ShouldBe(RecoveryReservationKind.NoOp);
    }

    [Fact]
    public void Backlogged_older_envelopes_do_not_create_a_burst_of_reads()
    {
        var schedule = RecoverySchedule.Create(Guid.NewGuid(), At).Value;
        schedule.Reserve(0, At.AddSeconds(90)).Kind.ShouldBe(RecoveryReservationKind.NoOp);
        var latest = schedule.Reserve(2, At.AddSeconds(90));
        latest.Kind.ShouldBe(RecoveryReservationKind.Read);
        latest.UpdatedSchedule.ConsumedMask.ShouldBe(7);
        latest.ConsumedSlots.ToArray().ShouldBe(new[] { 0, 1, 2 });
        latest
            .UpdatedSchedule.Reserve(1, At.AddSeconds(91))
            .Kind.ShouldBe(RecoveryReservationKind.NoOp);
    }

    [Fact]
    public void Deadline_never_starts_a_new_automatic_read()
    {
        var schedule = RecoverySchedule.Create(Guid.NewGuid(), At).Value;
        schedule.Reserve(3, At.AddSeconds(310)).Kind.ShouldBe(RecoveryReservationKind.Manual);
        var late = schedule.Reserve(3, At.AddSeconds(309));
        late.Kind.ShouldBe(RecoveryReservationKind.Read);
        late.UpdatedSchedule.ReadWindowUntil.ShouldBe(At.AddSeconds(310));
    }

    [Fact]
    public void Completing_a_matching_read_releases_the_shared_window_early()
    {
        var first = RecoverySchedule.Create(Guid.NewGuid(), At).Value.Reserve(0, At.AddSeconds(2));
        var closed = first.UpdatedSchedule.CompleteWindow(At.AddSeconds(2));
        closed.ReadWindowUntil.ShouldBeNull();
        closed.Reserve(1, At.AddSeconds(10)).Kind.ShouldBe(RecoveryReservationKind.Read);
    }

    [Fact]
    public void A_late_old_completion_cannot_close_the_new_read_window()
    {
        var first = RecoverySchedule.Create(Guid.NewGuid(), At).Value.Reserve(0, At.AddSeconds(2));
        var next = first.UpdatedSchedule.Reserve(1, At.AddSeconds(12));
        next.Kind.ShouldBe(RecoveryReservationKind.Read);
        var unchanged = next.UpdatedSchedule.CompleteWindow(At.AddSeconds(2));
        unchanged.ReadWindowUntil.ShouldBe(At.AddSeconds(22));
    }

    [Fact]
    public void At_most_four_distinct_automatic_reads_are_reserved()
    {
        var schedule = RecoverySchedule.Create(Guid.NewGuid(), At).Value;
        foreach (var (slot, seconds) in new[] { (0, 2), (1, 12), (2, 60), (3, 300) })
        {
            var reservation = schedule.Reserve(slot, At.AddSeconds(seconds));
            reservation.Kind.ShouldBe(RecoveryReservationKind.Read);
            schedule = reservation.UpdatedSchedule;
        }
        schedule.ConsumedMask.ShouldBe(15);
        schedule.Reserve(3, At.AddSeconds(301)).Kind.ShouldBe(RecoveryReservationKind.NoOp);
        schedule.Reserve(4, At.AddSeconds(301)).Kind.ShouldBe(RecoveryReservationKind.NoOp);
    }
}
