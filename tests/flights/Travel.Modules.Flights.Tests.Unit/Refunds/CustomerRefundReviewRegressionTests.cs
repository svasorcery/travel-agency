using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundReviewRegressionTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("pending")]
    [InlineData("mismatch")]
    public void Late_nonterminal_result_preserves_one_shot_manual_escalation(string kind)
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundDeadline(c.Id, c.DeadlineAt, c.DeadlineAt)
        );
        var now = c.DeadlineAt.AddSeconds(1);
        var result =
            kind == "null"
                ? null
                : CustomerRefundDecisionTests.Receipt(c, CustomerRefundState.Pending, now);
        if (kind == "mismatch")
            result = result! with { Amount = RefundTestBookings.Money(110) };
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundOutcome(c.Id, RefundTestBookings.Sender, result, now)
        );
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
        b.CustomerRefund.NeedsManualReview.ShouldBeTrue();
        b.CustomerRefund.DeadlineAt.ShouldBe(c.DeadlineAt);
        b.DecideCustomerRefundClaim(c.Id, b.CustomerRefund.Revision, Guid.NewGuid(), now)
            .Value.Events.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deadline_escalates_after_an_earlier_unknown_or_pending_result(bool pending)
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        var now = c.DeadlineAt.AddSeconds(-1);
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundOutcome(
                c.Id,
                RefundTestBookings.Sender,
                pending
                    ? CustomerRefundDecisionTests.Receipt(c, CustomerRefundState.Pending, now)
                    : null,
                now
            )
        );
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundDeadline(c.Id, c.DeadlineAt, c.DeadlineAt)
        );
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
    }

    [Fact]
    public void Matching_late_positive_receipt_can_close_manual_without_a_new_dispatch()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundDeadline(c.Id, c.DeadlineAt, c.DeadlineAt)
        );
        var now = c.DeadlineAt.AddSeconds(1);
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundOutcome(
                c.Id,
                RefundTestBookings.Sender,
                CustomerRefundDecisionTests.Receipt(c, CustomerRefundState.Sent, now),
                now
            )
        );
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.Sent);
        b.CustomerRefund.NeedsManualReview.ShouldBeFalse();
        b.DecideCustomerRefundClaim(c.Id, b.CustomerRefund.Revision, Guid.NewGuid(), now)
            .Value.Events.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("80.00000000000000000000000000001")]
    [InlineData("0.00000000000000000000000000001")]
    public void Manual_exact_money_is_rejected_instead_of_silently_rounded(string amount) =>
        Request(amount).ToInput().IsError.ShouldBeTrue();

    [Theory]
    [InlineData("80")]
    [InlineData("80.0")]
    [InlineData("00080.00000000000000000000000000000")]
    public void Representable_equivalent_money_remains_equivalent(string amount) =>
        Request(amount).ToInput().Value.Observation!.Amount.Amount.ShouldBe(80m);

    [Theory]
    [InlineData("evidence_fictional", false)]
    [InlineData("EVIDENCE-FICTIONAL", true)]
    public void Http_evidence_reference_follows_the_documented_contract(
        string reference,
        bool valid
    ) =>
        new CustomerRefundReviewRequest(
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            "Inconclusive",
            reference
        )
            .ToInput()
            .IsError.ShouldBe(!valid);

    [Fact]
    public void Report_blocks_consent_and_dispatch_of_a_previously_prepared_order()
    {
        var b = Prepared(false);
        Report(b);
        var op = b.CurrentCancellation!;
        var t = op.Terms!;
        b.DecideCancellationConsent(
                RefundTestBookings.Owner,
                op.Id,
                op.Revision,
                t.Revision,
                t.Hash,
                t.NoticeVersion,
                new string('b', 64),
                Guid.NewGuid(),
                RefundTestBookings.Now.AddSeconds(9)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        b = Prepared(true);
        Report(b);
        op = b.CurrentCancellation!;
        b.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                RefundTestBookings.Sender,
                Guid.NewGuid(),
                RefundTestBookings.Now.AddSeconds(9)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public async Task Report_committed_between_claim_and_call_leaves_the_sender_uncertain_and_sends_nothing()
    {
        var b = Prepared(true);
        var op = b.CurrentCancellation!;
        var events = new List<object>();
        var calls = 0;
        var provider = (IFlightCancellationProvider)
            CustomerRefundOrchestrationTests.Proxy.Make(
                typeof(IFlightCancellationProvider),
                (m, _) =>
                {
                    if (m.Name == "InspectOrderAsync")
                        return Task.FromResult<ErrorOr<CancellationEligibility>>(
                            new CancellationEligibility(
                                RefundTestBookings.Order,
                                true,
                                CancellationProviderPaymentState.Paid,
                                RefundTestBookings.Money(100),
                                new(
                                    SettlementComposition.CashOnly,
                                    true,
                                    CancellationResolutionSource.SupplierApi,
                                    false,
                                    false
                                )
                            )
                        );
                    if (m.Name == "ConfirmAsync")
                    {
                        calls++;
                        return Task.FromResult<ErrorOr<CancellationEffectResult>>(
                            new CancellationEffectResult(
                                CancellationEffectOutcome.Unknown,
                                null,
                                CancellationReason.ProviderUnavailable
                            )
                        );
                    }
                    throw new NotSupportedException(m.Name);
                }
            );
        var saves = 0;
        await ExecuteCancellationConfirmationHandler.Handle(
            new(b.Id, op.Id, op.ConfirmationAdmissionId!.Value),
            CustomerRefundExecutionTests.Session(b, events, () => { }),
            CustomerRefundExecutionTests.Store(
                b,
                events,
                () =>
                {
                    if (++saves == 1)
                        Report(b);
                }
            ),
            CustomerRefundOrchestrationTests.Outbox([]),
            provider,
            new Sender(),
            new CustomerRefundExecutionTests.Clock(),
            CancellationToken.None
        );
        calls.ShouldBe(0);
        b.HasSupplierCancellationReportBarrier.ShouldBeTrue();
        b.CurrentCancellation!.ConfirmationDispatchedAt.ShouldNotBeNull();
        b.CurrentCancellation.IsTerminal.ShouldBeFalse();
        b.CustomerRefund.ShouldBeNull();
    }

    private static CustomerRefundReviewRequest Request(string amount) =>
        new(
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            "Sent",
            "EVIDENCE-FICTIONAL",
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                amount,
                "GBP",
                "OriginalFictionalPaymentMethod",
                "Sent",
                "receipt_fictional",
                RefundTestBookings.Now
            )
        );

    private static BookingAggregate Prepared(bool accepted)
    {
        var b = RefundTestBookings.Held();
        var scope = CancellationScope.Create(b).Value;
        RefundTestBookings.Apply(
            b,
            new CancellationPreparationStarted(
                RefundTestBookings.Cancellation,
                RefundTestBookings.Owner,
                RefundTestBookings.Order,
                scope,
                new string('a', 64),
                Guid.NewGuid(),
                RefundTestBookings.Now
            )
        );
        var t = CancellationTerms
            .Create(
                new(
                    1,
                    b.Id,
                    RefundTestBookings.Owner,
                    RefundTestBookings.Cancellation,
                    RefundTestBookings.Order,
                    "ore_fictional",
                    scope,
                    RefundTestBookings.Money(80),
                    CancellationRefundDestination.Balance,
                    new(
                        SettlementComposition.CashOnly,
                        true,
                        CancellationResolutionSource.SupplierApi,
                        false,
                        false
                    ),
                    RefundTestBookings.Now.AddMinutes(10),
                    "cancellation-v1"
                ),
                RefundTestBookings.Now
            )
            .Value;
        RefundTestBookings.Apply(
            b,
            new CancellationTermsObtained(
                RefundTestBookings.Cancellation,
                t,
                RefundTestBookings.Now
            )
        );
        if (accepted)
            RefundTestBookings.Apply(
                b,
                new CancellationTermsAccepted(
                    RefundTestBookings.Cancellation,
                    1,
                    t.Hash,
                    t.NoticeVersion,
                    new string('b', 64),
                    Guid.NewGuid(),
                    RefundTestBookings.Now
                )
            );
        return b;
    }

    private static void Report(BookingAggregate b) =>
        RefundTestBookings.Apply(
            b,
            new SupplierCancellationReported(
                Guid.NewGuid(),
                RefundTestBookings.Owner,
                RefundTestBookings.Order,
                RefundTestBookings.Now.AddSeconds(1)
            )
        );

    private sealed class Sender : IDispatchInstanceIdentity
    {
        public Guid Id => RefundTestBookings.Sender;
    }
}
