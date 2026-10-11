using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Handlers.Refunds;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundStatusTests
{
    [Fact]
    public async Task Eligible_cancellation_assessment_schedules_one_retained_refund()
    {
        var b = RefundTestBookings.Cancelled();
        var events = new List<object>();
        var work = new List<(object Message, Wolverine.DeliveryOptions? Options)>();
        var session = CustomerRefundExecutionTests.Session(b, events, () => { });
        await AssessCustomerRefundHandler.Handle(
            new(b.Id, RefundTestBookings.Cancellation),
            session,
            CustomerRefundOrchestrationTests.Outbox(work),
            new(true),
            new CustomerRefundExecutionTests.Clock(),
            CancellationToken.None
        );
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.Ready);
        work.Count(x => x.Message is ExecuteCustomerRefund).ShouldBe(1);
        await AssessCustomerRefundHandler.Handle(
            new(b.Id, RefundTestBookings.Cancellation),
            session,
            CustomerRefundOrchestrationTests.Outbox(work),
            new(true),
            new CustomerRefundExecutionTests.Clock(),
            CancellationToken.None
        );
        work.Count(x => x.Message is ExecuteCustomerRefund).ShouldBe(1);
    }

    [Fact]
    public void Authoritative_sent_status_excludes_payment_and_operation_references()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        var at = RefundTestBookings.Now.AddSeconds(9);
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundOutcome(
                c.Id,
                RefundTestBookings.Sender,
                CustomerRefundDecisionTests.Receipt(c, CustomerRefundState.Sent, at),
                at
            )
        );
        var status = CustomerRefundStatusFactory.Create(b, 100, at);
        status.State.ShouldBe("Sent");
        status.Amount!.Amount.ShouldBe("80");
        status.SupplierRefund!.Amount.Amount.ShouldBe("80");
        status.SupplierSettlement.ShouldBe("NotEstablished");
        status.CustomerCredit.ShouldBe("NotEstablished");
        var wire = JsonSerializer.Serialize(status);
        wire.ShouldNotContain("PaymentReference");
        wire.ShouldNotContain(c.Id.ToString());
        wire.ShouldNotContain("ref_fictional_receipt");
    }

    [Fact]
    public async Task No_client_activity_is_needed_for_fixed_deadline_escalation()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var c = b.CustomerRefund!;
        await CustomerRefundDeadlineHandler.Handle(
            new(b.Id, c.Id, c.DeadlineAt),
            CustomerRefundExecutionTests.Session(b, [], () => { }),
            CustomerRefundOrchestrationTests.Outbox([]),
            new DeadlineClock(c.DeadlineAt),
            CancellationToken.None
        );
        b.CustomerRefund!.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
        b.CustomerRefund.DeadlineAt.ShouldBe(c.DeadlineAt);
    }

    private sealed class DeadlineClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
