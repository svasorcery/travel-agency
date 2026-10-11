using Shouldly;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundReviewTests
{
    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000901");

    [Fact]
    public void Positive_manual_receipt_is_bound_to_the_original_case_and_actor()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        var at = RefundTestBookings.Now.AddSeconds(9);
        var input = Positive(c, at);
        CustomerRefundDecisionTests.Apply(b, b.DecideCustomerRefundReview(Actor, input, at));
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.Sent);
        b.CustomerRefund.Observation!.Source.ShouldBe(
            CustomerRefundEvidenceSource.OperatorVerified
        );
        b.DecideCustomerRefundReview(Actor, input, at.AddDays(2)).Value.Events.Count.ShouldBe(0);
        b.DecideCustomerRefundReview(Guid.NewGuid(), input, at).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Changed_payload_under_an_accepted_resolution_identity_conflicts()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        var at = RefundTestBookings.Now.AddSeconds(9);
        var input = Positive(c, at);
        CustomerRefundDecisionTests.Apply(b, b.DecideCustomerRefundReview(Actor, input, at));
        var changed = input with { EvidenceReference = "EVIDENCE-FICTIONAL-OTHER" };
        b.DecideCustomerRefundReview(Actor, changed, at).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Negative_manual_outcome_requires_matching_sender_quiescence()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        var at = RefundTestBookings.Now.AddSeconds(9);
        var input = Positive(c, at) with
        {
            Decision = CustomerRefundReviewKind.ConfirmedNoEffect,
            Observation = CustomerRefundDecisionTests.Receipt(
                c,
                CustomerRefundState.ConfirmedNoEffect,
                at
            ) with
            {
                Source = CustomerRefundEvidenceSource.OperatorVerified,
                NoPendingOutcome = true,
            },
        };
        b.DecideCustomerRefundReview(Actor, input, at).IsError.ShouldBeTrue();
        var stopped = input with
        {
            SenderStop = new(RefundTestBookings.Sender, at, true, true, true, true),
        };
        CustomerRefundDecisionTests.Apply(b, b.DecideCustomerRefundReview(Actor, stopped, at));
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.ConfirmedNoEffect);
        b.DecideCustomerRefundClaim(c.Id, b.CustomerRefund.Revision, Guid.NewGuid(), at)
            .Value.Events.Count.ShouldBe(0);
    }

    [Fact]
    public void Inconclusive_review_retains_unknown_and_cannot_seed_historical_payment()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        var at = RefundTestBookings.Now.AddSeconds(9);
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundOutcome(c.Id, RefundTestBookings.Sender, null, at)
        );
        var input = new CustomerRefundReviewInput(
            c.Id,
            b.CustomerRefund!.Revision,
            Guid.NewGuid(),
            CustomerRefundReviewKind.Inconclusive,
            "EVIDENCE-FICTIONAL-UNKNOWN"
        );
        CustomerRefundDecisionTests.Apply(b, b.DecideCustomerRefundReview(Actor, input, at));
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.Unknown);
        b.CustomerRefund.HadUnknown.ShouldBeTrue();
    }

    private static CustomerRefundReviewInput Positive(CustomerRefundCase c, DateTimeOffset at) =>
        new(
            c.Id,
            c.Revision,
            Guid.NewGuid(),
            CustomerRefundReviewKind.Sent,
            "EVIDENCE-FICTIONAL-REFUND",
            CustomerRefundDecisionTests.Receipt(c, CustomerRefundState.Sent, at) with
            {
                Source = CustomerRefundEvidenceSource.OperatorVerified,
            }
        );
}
