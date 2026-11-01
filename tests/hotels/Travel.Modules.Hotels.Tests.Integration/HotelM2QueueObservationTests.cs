using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Webhooks;
using Wolverine;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2QueueObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reservation = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static Envelope Work(bool escalation = false, bool camel = true)
    {
        var work = new HotelWork(Reservation, escalation, Now.AddSeconds(-1));
        return new Envelope(work)
        {
            Id = Guid.NewGuid(),
            ScheduledTime = work.DueAt,
            Data = JsonSerializer.SerializeToUtf8Bytes(
                work,
                camel
                    ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    : new JsonSerializerOptions()
            ),
        };
    }

    private static HotelM2QueueRow Row(
        ReservationOutcome outcome = ReservationOutcome.Confirmed,
        bool attempt = true,
        bool booking = true
    ) => new(Reservation, attempt, booking, outcome);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actual_Envelope_type_and_both_JSON_casings_prove_terminal_read_noop(bool camel)
    {
        var q = HotelsM2QueueObservation.Capture([Work(camel: camel)], [Row()], Now);
        q.RawDue.ShouldBe(1);
        q.RedundantDue.ShouldBe(1);
        q.RequiredDue.ShouldBe(0);
        q.UnknownDue.ShouldBe(0);
        q.Complete.ShouldBeTrue();
        q.Items[0].ReservationId.ShouldBe(Reservation);
        q.Items[0].Reason.ShouldBe("TerminalReservation");
        HotelsM2QueueObservation.Settled(q, 0, 1).ShouldBeTrue();
        HotelsM2QueueObservation.Settled(q, 1, 1).ShouldBeFalse();
        HotelsM2QueueObservation.Settled(q, 0, 0).ShouldBeFalse();
    }

    [Theory]
    [InlineData(ReservationOutcome.Quoted)]
    [InlineData(ReservationOutcome.Submitted)]
    [InlineData(ReservationOutcome.Pending)]
    [InlineData(ReservationOutcome.Unknown)]
    [InlineData(ReservationOutcome.ReservedAwaitingDetails)]
    public void Nonterminal_work_remains_required(ReservationOutcome outcome)
    {
        var q = HotelsM2QueueObservation.Capture([Work()], [Row(outcome)], Now);
        q.RequiredDue.ShouldBe(1);
        HotelsM2QueueObservation.Settled(q, 0, 1).ShouldBeFalse();
    }

    [Fact]
    public void Cancelled_read_is_noop_but_escalation_is_never_excluded()
    {
        HotelsM2QueueObservation
            .Capture([Work()], [Row(ReservationOutcome.Cancelled)], Now)
            .RedundantDue.ShouldBe(1);
        var q = HotelsM2QueueObservation.Capture([Work(true)], [Row()], Now);
        q.RequiredDue.ShouldBe(1);
        HotelsM2QueueObservation.Settled(q, 0, 1).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Incomplete_terminal_identity_does_not_qualify(bool attempt, bool booking)
    {
        var q = HotelsM2QueueObservation.Capture(
            [Work()],
            [Row(attempt: attempt, booking: booking)],
            Now
        );
        q.RequiredDue.ShouldBe(1);
        HotelsM2QueueObservation.Settled(q, 0, 1).ShouldBeFalse();
    }

    [Fact]
    public void Missing_owner_row_and_unknown_type_fail_closed()
    {
        var missing = HotelsM2QueueObservation.Capture([Work()], [], Now);
        missing.UnknownDue.ShouldBe(1);
        missing.Complete.ShouldBeFalse();
        var envelope = Work();
        envelope.MessageType = "Some.Other.Hotels.Work";
        var unknown = HotelsM2QueueObservation.Capture([envelope], [Row()], Now);
        unknown.UnknownDue.ShouldBe(1);
        HotelsM2QueueObservation.Settled(unknown, 0, 1).ShouldBeFalse();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(
        "{\"reservationId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"dueAt\":\"2026-11-01T11:59:59Z\"}"
    )]
    [InlineData(
        "{\"reservationId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"escalation\":false,\"dueAt\":\"2026-11-01T11:59:59Z\",\"extra\":\"hidden\"}"
    )]
    [InlineData(
        "{\"reservationId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"escalation\":false,\"escalation\":true,\"dueAt\":\"2026-11-01T11:59:59Z\"}"
    )]
    [InlineData("not-json")]
    public void Malformed_or_partial_work_cannot_be_declared_redundant(string body)
    {
        var envelope = Work();
        envelope.Data = System.Text.Encoding.UTF8.GetBytes(body);
        var q = HotelsM2QueueObservation.Capture([envelope], [Row()], Now);
        q.UnknownDue.ShouldBe(1);
        HotelsM2QueueObservation.Valid(q).ShouldBeFalse();
        JsonSerializer.Serialize(q).ShouldNotContain("hidden");
        JsonSerializer.Serialize(q).ShouldNotContain("not-json");
    }

    [Fact]
    public void Missing_oversized_and_overflowing_evidence_stays_in_denominator()
    {
        var missing = Work();
        missing.Data = null;
        HotelsM2QueueObservation.Capture([missing], [Row()], Now).UnknownDue.ShouldBe(1);
        var large = Work();
        large.Data = new byte[2049];
        HotelsM2QueueObservation.Capture([large], [Row()], Now).UnknownDue.ShouldBe(1);
        var q = HotelsM2QueueObservation.Capture(
            Enumerable.Range(0, 65).Select(_ => Work()),
            [Row()],
            Now
        );
        q.RawDue.ShouldBe(65);
        q.Items.Length.ShouldBe(64);
        q.UnknownDue.ShouldBe(1);
        HotelsM2QueueObservation.Valid(q).ShouldBeFalse();
    }

    [Fact]
    public void Future_and_handled_envelopes_are_not_due_and_webhook_work_remains_required()
    {
        var future = Work();
        future.ScheduledTime = Now.AddSeconds(1);
        var handled = Work();
        handled.Status = EnvelopeStatus.Handled;
        var hook = new ProcessHotelObservation(Guid.NewGuid());
        var pending = new Envelope(hook)
        {
            Id = Guid.NewGuid(),
            ScheduledTime = Now.AddSeconds(-1),
            Data = JsonSerializer.SerializeToUtf8Bytes(hook),
        };
        var q = HotelsM2QueueObservation.Capture([future, handled, pending], [Row()], Now);
        q.RawDue.ShouldBe(1);
        q.RequiredDue.ShouldBe(1);
        q.Items[0].Reason.ShouldBe("WebhookObservation");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Empty_durable_identity_must_not_be_excluded(int kind)
    {
        var row = HotelM2QueueRow.Capture(
            Reservation,
            kind == 0 ? Guid.Empty : Guid.NewGuid(),
            kind == 1 ? ""
                : kind == 2 ? "   "
                : "OWN-booking",
            ReservationOutcome.Confirmed
        );
        var q = HotelsM2QueueObservation.Capture([Work()], [row], Now);
        q.RedundantDue.ShouldBe(0);
        q.RequiredDue.ShouldBe(1);
        HotelsM2QueueObservation.Settled(q, 0, 1).ShouldBeFalse();
    }
}
