using Shouldly;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundDecisionTests
{
    [Fact]
    public void Admission_retains_one_exact_case_across_new_request_identities()
    {
        var booking = RefundTestBookings.Cancelled();
        var id = Guid.NewGuid();
        var first = booking.DecideCustomerRefundAdmission(
            RefundTestBookings.Owner,
            RefundTestBookings.Cancellation,
            id,
            RefundTestBookings.Now.AddSeconds(6)
        );
        first.IsError.ShouldBeFalse();
        first.Value.Events.Count.ShouldBe(1);
        foreach (var e in first.Value.Events)
            RefundTestBookings.Apply(booking, e);
        booking.CustomerRefund!.Amount!.Amount.ShouldBe(80m);
        var replay = booking.DecideCustomerRefundAdmission(
            RefundTestBookings.Owner,
            RefundTestBookings.Cancellation,
            Guid.NewGuid(),
            RefundTestBookings.Now.AddDays(2)
        );
        replay.Value.Events.Count.ShouldBe(0);
        booking.CustomerRefund.Id.ShouldBe(id);
    }

    [Fact]
    public void Dispatch_claim_is_not_issued_twice_or_after_the_fixed_deadline()
    {
        var booking = Admitted();
        var current = booking.CustomerRefund!;
        Apply(
            booking,
            booking.DecideCustomerRefundClaim(
                current.Id,
                current.Revision,
                RefundTestBookings.Sender,
                RefundTestBookings.Now.AddSeconds(7)
            )
        );
        booking.CustomerRefund!.State.ShouldBe(CustomerRefundState.DispatchClaimed);
        booking
            .DecideCustomerRefundClaim(
                current.Id,
                booking.CustomerRefund.Revision,
                Guid.NewGuid(),
                RefundTestBookings.Now.AddSeconds(8)
            )
            .Value.Events.Count.ShouldBe(0);
        var expired = Admitted();
        expired
            .DecideCustomerRefundClaim(
                expired.CustomerRefund!.Id,
                expired.CustomerRefund.Revision,
                RefundTestBookings.Sender,
                RefundTestBookings.Now.AddMinutes(4)
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Deadline_keeps_unknown_without_another_payment_permission()
    {
        var booking = Claimed();
        var current = booking.CustomerRefund!;
        Apply(
            booking,
            booking.DecideCustomerRefundDeadline(current.Id, current.DeadlineAt, current.DeadlineAt)
        );
        booking.CustomerRefund!.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
        booking.CustomerRefund.HadUnknown.ShouldBeTrue();
        booking
            .DecideCustomerRefundClaim(
                current.Id,
                booking.CustomerRefund.Revision,
                RefundTestBookings.Sender,
                current.DeadlineAt.AddDays(1)
            )
            .Value.Events.Count.ShouldBe(0);
    }

    [Fact]
    public void Matching_late_receipt_establishes_sent_without_changing_booking_status()
    {
        var booking = Claimed();
        var current = booking.CustomerRefund!;
        Apply(
            booking,
            booking.DecideCustomerRefundDeadline(current.Id, current.DeadlineAt, current.DeadlineAt)
        );
        var observedAt = current.DeadlineAt.AddSeconds(1);
        Apply(
            booking,
            booking.DecideCustomerRefundOutcome(
                current.Id,
                RefundTestBookings.Sender,
                Receipt(current, CustomerRefundState.Sent, observedAt),
                observedAt
            )
        );
        booking.CustomerRefund!.State.ShouldBe(CustomerRefundState.Sent);
        booking.Status.ShouldBe(Core.Aggregates.BookingStatus.Cancelled);
        booking.RefundedAt.ShouldBeNull();
    }

    [Fact]
    public void Mismatched_money_is_not_accepted_as_a_successful_refund()
    {
        var booking = Claimed();
        var current = booking.CustomerRefund!;
        var receipt = Receipt(
            current,
            CustomerRefundState.Sent,
            RefundTestBookings.Now.AddSeconds(8)
        ) with
        {
            Amount = RefundTestBookings.Money(100m),
        };
        Apply(
            booking,
            booking.DecideCustomerRefundOutcome(
                current.Id,
                RefundTestBookings.Sender,
                receipt,
                RefundTestBookings.Now.AddSeconds(8)
            )
        );
        booking.CustomerRefund!.State.ShouldBe(CustomerRefundState.Unknown);
        booking.CustomerRefund.Observation.ShouldBeNull();
    }

    [Fact]
    public void Pending_is_not_sent_and_unknown_is_not_erased_by_a_late_refusal()
    {
        var booking = Claimed();
        var current = booking.CustomerRefund!;
        var at = RefundTestBookings.Now.AddSeconds(8);
        Apply(
            booking,
            booking.DecideCustomerRefundOutcome(
                current.Id,
                RefundTestBookings.Sender,
                Receipt(current, CustomerRefundState.Pending, at),
                at
            )
        );
        booking.CustomerRefund!.State.ShouldBe(CustomerRefundState.Pending);
        Apply(
            booking,
            booking.DecideCustomerRefundOutcome(current.Id, RefundTestBookings.Sender, null, at)
        );
        booking.CustomerRefund!.State.ShouldBe(CustomerRefundState.Unknown);
        Apply(
            booking,
            booking.DecideCustomerRefundOutcome(
                current.Id,
                RefundTestBookings.Sender,
                Receipt(current, CustomerRefundState.ConfirmedNoEffect, at) with
                {
                    NoPendingOutcome = true,
                },
                at
            )
        );
        booking.CustomerRefund!.State.ShouldNotBe(CustomerRefundState.ConfirmedNoEffect);
    }

    [Fact]
    public void Foreign_owner_cannot_admit_or_replay_an_owned_refund()
    {
        var booking = Admitted();
        booking
            .DecideCustomerRefundAdmission(
                Guid.NewGuid(),
                RefundTestBookings.Cancellation,
                booking.CustomerRefund!.Id,
                RefundTestBookings.Now.AddSeconds(9)
            )
            .IsError.ShouldBeTrue();
    }

    internal static Core.Aggregates.BookingAggregate Admitted()
    {
        var b = RefundTestBookings.Cancelled();
        Apply(
            b,
            b.DecideCustomerRefundAdmission(
                RefundTestBookings.Owner,
                RefundTestBookings.Cancellation,
                Guid.NewGuid(),
                RefundTestBookings.Now.AddSeconds(6)
            )
        );
        return b;
    }

    internal static Core.Aggregates.BookingAggregate Claimed()
    {
        var b = Admitted();
        var c = b.CustomerRefund!;
        Apply(
            b,
            b.DecideCustomerRefundClaim(
                c.Id,
                c.Revision,
                RefundTestBookings.Sender,
                RefundTestBookings.Now.AddSeconds(7)
            )
        );
        return b;
    }

    internal static CustomerRefundObservation Receipt(
        CustomerRefundCase c,
        CustomerRefundState state,
        DateTimeOffset at
    ) =>
        new(
            c.Id,
            c.Basis!.PaymentReference,
            c.Amount!,
            CustomerRefundDestination.OriginalFictionalPaymentMethod,
            state,
            "ref_fictional_receipt",
            at,
            CustomerRefundEvidenceSource.FictionalGateway
        );

    internal static void Apply(
        Core.Aggregates.BookingAggregate b,
        ErrorOr.ErrorOr<CustomerRefundDecision> result
    )
    {
        result.IsError.ShouldBeFalse();
        foreach (var e in result.Value.Events)
            RefundTestBookings.Apply(b, e);
    }
}
