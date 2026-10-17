using Shouldly;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class ReservationStateTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_identity_hours_after_escalation_gets_one_read_window()
    {
        var state = new ReservationState
        {
            Outcome = ReservationOutcome.Submitted,
            EscalateAt = Now.AddMinutes(10),
        };
        state.Escalate(Now.AddHours(1));
        state.Review.ShouldContain(ReviewReason.Deadline);
        state.Apply(
            new(ObservationKind.ReservedWithoutDetails, new("book_1", null, null)),
            Terms(),
            Now.AddHours(2)
        );
        state.FirstKnownAt.ShouldBe(Now.AddHours(2));
        state.ReadUntil.ShouldBe(Now.AddHours(2).AddMinutes(5));
        state.ClaimRead(Now.AddHours(2)).ShouldBeTrue();
        state.ClaimRead(Now.AddHours(2)).ShouldBeFalse();
        state.ClaimRead(Now.AddHours(2).AddSeconds(30)).ShouldBeTrue();
        state.ClaimRead(Now.AddHours(2).AddSeconds(150)).ShouldBeTrue();
        state.ClaimRead(Now.AddHours(2).AddSeconds(200)).ShouldBeFalse();
        var until = state.ReadUntil;
        state.Apply(
            new(ObservationKind.ReservedWithoutDetails, new("book_1", null, null)),
            Terms(),
            Now.AddHours(3)
        );
        state.ReadUntil.ShouldBe(until);
        state.ReadsClaimed.ShouldBe(3);
        state.Apply(
            new(ObservationKind.Confirmed, new("book_1", "hotel_ref", Terms())),
            Terms(),
            Now.AddHours(3)
        );
        state.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        state.Review.ShouldBeEmpty();
    }

    [Fact]
    public void Cancellation_is_not_reactivated_and_conflicts_stay_sticky()
    {
        var terms = Terms();
        var state = new ReservationState { Outcome = ReservationOutcome.Submitted };
        state.Apply(new(ObservationKind.NotCreated), terms, Now);
        state.Apply(
            new(ObservationKind.Confirmed, new("book_1", "ref", terms)),
            terms,
            Now.AddMinutes(1)
        );
        state.Review.ShouldContain(ReviewReason.Conflict);
        state.Apply(
            new(ObservationKind.Cancelled, new("book_1", null, null)),
            terms,
            Now.AddMinutes(2)
        );
        state.Apply(
            new(ObservationKind.Confirmed, new("book_1", "ref", terms)),
            terms,
            Now.AddMinutes(3)
        );
        state.Outcome.ShouldBe(ReservationOutcome.Cancelled);
        state.EverReserved.ShouldBeTrue();
        state.Active.ShouldBeFalse();
        state.Review.ShouldContain(ReviewReason.Conflict);
    }

    [Fact]
    public void Missing_identity_never_reads_and_mismatch_survives_later_match()
    {
        var state = new ReservationState { Outcome = ReservationOutcome.Submitted };
        state.ClaimRead(Now).ShouldBeFalse();
        var terms = Terms();
        state.Apply(
            new(ObservationKind.Confirmed, new("book_1", "ref", terms with { Room = "Different" })),
            terms,
            Now
        );
        state.Review.ShouldContain(ReviewReason.TermsMismatch);
        state.Apply(new(ObservationKind.Confirmed, new("book_1", "ref", terms)), terms, Now);
        state.Apply(new(ObservationKind.Unknown), terms, Now);
        state.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        state.Review.ShouldContain(ReviewReason.TermsMismatch);
        // Current facts may improve, but the reason for mandatory review must remain inspectable.
        state.Facts!.Actual!.Room.ShouldBe("Double");
        var saved = System.Text.Json.JsonSerializer.SerializeToElement(state);
        saved.TryGetProperty("FirstMismatch", out var mismatch).ShouldBeTrue();
        mismatch.GetProperty("Actual").GetProperty("Room").GetString().ShouldBe("Different");
        mismatch.GetProperty("ObservedAt").GetDateTimeOffset().ShouldBe(Now);
    }

    internal static StayTerms Terms() =>
        new(
            new("prop_1", "Fictional Garden Hotel", "Fictional address", null, null),
            "Double",
            "rate_1",
            new(new(2027, 1, 3), new(2027, 1, 5)),
            new(1),
            new(100.25m, "GBP"),
            [],
            [new("Breakfast", "1 adult, whole stay")],
            new("NonRefundable", []),
            [new("Adult check-in", "Lead guest")],
            true,
            "ImmediateTest"
        );

    [Fact]
    public void Reserved_without_identity_is_positive_and_nullable_reference_does_not_block_confirmation()
    {
        var state = new ReservationState { Outcome = ReservationOutcome.Submitted };
        state.Apply(new(ObservationKind.ReservedWithoutDetails), Terms(), Now);
        state.Outcome.ShouldBe(ReservationOutcome.ReservedAwaitingDetails);
        state.EverReserved.ShouldBeTrue();
        state.BoundBookingId.ShouldBeNull();
        state.ClaimRead(Now).ShouldBeFalse();
        state.Apply(
            new(
                ObservationKind.Confirmed,
                new(
                    "book_1",
                    null,
                    Terms() with
                    {
                        RateId = "new_stage_id",
                        Property = Terms().Property with { Name = "Updated display name" },
                    }
                )
            ),
            Terms(),
            Now.AddHours(2)
        );
        state.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        state.Review.ShouldBeEmpty();
        state.Facts!.HotelReference.ShouldBeNull();
    }

    [Theory]
    [InlineData(ObservationKind.Confirmed)]
    [InlineData(ObservationKind.ReservedWithoutDetails)]
    public void Current_confirmation_after_cancellation_conflicts_but_old_created_does_not(
        ObservationKind kind
    )
    {
        var state = new ReservationState { Outcome = ReservationOutcome.Submitted };
        state.Apply(new(ObservationKind.Cancelled, new("book_1", null, Terms())), Terms(), Now);
        state.Apply(
            new(kind, new("book_1", null, kind == ObservationKind.Confirmed ? Terms() : null)),
            Terms(),
            Now.AddMinutes(1)
        );
        state.Review.ShouldNotContain(ReviewReason.Conflict);
        state.Apply(
            new(
                kind,
                new("book_1", null, kind == ObservationKind.Confirmed ? Terms() : null),
                CurrentRead: true
            ),
            Terms(),
            Now.AddMinutes(2)
        );
        state.Outcome.ShouldBe(ReservationOutcome.Cancelled);
        state.Review.ShouldContain(ReviewReason.Conflict);
        state.Facts!.Actual.ShouldNotBeNull();
    }

    [Fact]
    public void Early_no_creation_waits_until_original_sender_finishes()
    {
        var state = new ReservationState { Outcome = ReservationOutcome.Submitted };
        state.Apply(new(ObservationKind.NotCreated, SenderFinished: false), Terms(), Now);
        state.Outcome.ShouldBe(ReservationOutcome.Submitted);
        state.Apply(new(ObservationKind.Unknown, SenderFinished: true), Terms(), Now.AddMinutes(1));
        state.Outcome.ShouldBe(ReservationOutcome.NotCreated);
    }
}
