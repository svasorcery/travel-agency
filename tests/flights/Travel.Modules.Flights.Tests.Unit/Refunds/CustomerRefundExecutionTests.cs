using System.Reflection;
using ErrorOr;
using Marten;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Refunds;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Refunds;
using Wolverine;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundExecutionTests
{
    [Theory]
    [InlineData(false, CustomerRefundState.Sent)]
    [InlineData(true, CustomerRefundState.Unknown)]
    public async Task Retained_dispatch_is_not_executed_again_after_response_or_result_loss(
        bool lose,
        CustomerRefundState expected
    )
    {
        var b = CustomerRefundDecisionTests.Admitted();
        var c = b.CustomerRefund!;
        var events = new List<object>();
        var saved = 0;
        var gateway = new Gateway(lose);
        var session = Session(b, events, () => saved++);
        var store = Store(b, events, () => saved++);
        var outbox = CustomerRefundOrchestrationTests.Outbox([]);
        var message = new ExecuteCustomerRefund(b.Id, c.Id);
        await ExecuteCustomerRefundHandler.Handle(
            message,
            session,
            store,
            outbox,
            gateway,
            new Sender(),
            new Clock(),
            CancellationToken.None
        );
        gateway.Calls.ShouldBe(1);
        b.CustomerRefund!.State.ShouldBe(expected);
        saved.ShouldBe(2);
        await ExecuteCustomerRefundHandler.Handle(
            message,
            Session(b, events, () => saved++),
            store,
            outbox,
            gateway,
            new Sender(),
            new Clock(),
            CancellationToken.None
        );
        gateway.Calls.ShouldBe(1);
        saved.ShouldBe(2);
    }

    [Fact]
    public async Task Claim_commit_failure_never_reaches_the_gateway()
    {
        var b = CustomerRefundDecisionTests.Admitted();
        var events = new List<object>();
        var gateway = new Gateway(false);
        var session = Session(
            b,
            events,
            () => throw new InvalidOperationException("Synthetic commit failure")
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            ExecuteCustomerRefundHandler.Handle(
                new(b.Id, b.CustomerRefund!.Id),
                session,
                Store(b, events, () => { }),
                CustomerRefundOrchestrationTests.Outbox([]),
                gateway,
                new Sender(),
                new Clock(),
                CancellationToken.None
            )
        );
        gateway.Calls.ShouldBe(0);
        b.CustomerRefund!.ClaimedAt.ShouldBeNull();
    }

    internal static IDocumentStore Store(BookingAggregate b, List<object> events, Action save) =>
        (IDocumentStore)
            CustomerRefundOrchestrationTests.Proxy.Make(
                typeof(IDocumentStore),
                (m, _) =>
                    m.Name == "LightweightSession"
                        ? Session(b, events, save)
                        : throw new NotSupportedException(m.Name)
            );

    internal static IDocumentSession Session(
        BookingAggregate b,
        List<object> events,
        Action save
    ) =>
        (IDocumentSession)
            CustomerRefundOrchestrationTests.Proxy.Make(
                typeof(IDocumentSession),
                (m, _) =>
                {
                    if (m.Name == "DisposeAsync")
                        return ValueTask.CompletedTask;
                    if (m.Name == "Dispose")
                        return null;
                    if (m.Name == "SaveChangesAsync")
                    {
                        save();
                        return Task.CompletedTask;
                    }
                    if (m.Name != "get_Events")
                        throw new NotSupportedException(m.Name);
                    return CustomerRefundOrchestrationTests.Proxy.Make(
                        m.ReturnType,
                        (fetch, _) =>
                        {
                            if (fetch.Name != "FetchForWriting")
                                throw new NotSupportedException(fetch.Name);
                            var streamType = fetch.ReturnType.GetGenericArguments()[0];
                            var stream = CustomerRefundOrchestrationTests.Stream(b, events);
                            return typeof(Task)
                                .GetMethod(nameof(Task.FromResult))!
                                .MakeGenericMethod(streamType)
                                .Invoke(null, [stream]);
                        }
                    );
                }
            );

    private sealed class Sender : IDispatchInstanceIdentity
    {
        public Guid Id => RefundTestBookings.Sender;
    }

    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => RefundTestBookings.Now.AddSeconds(9);
    }

    private sealed class Gateway(bool lose) : ICustomerRefundGateway
    {
        internal int Calls;

        public Task<ErrorOr<CustomerRefundObservation>> ExecuteAsync(
            CustomerRefundRequest request,
            CancellationToken ct
        )
        {
            Calls++;
            request.Amount.Amount.ShouldBe(80m);
            request.OriginalPayment.CapturedAmount.Amount.ShouldBe(100m);
            return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
                lose
                    ? Error.Failure("Synthetic.ResultLost", "Synthetic result lost.")
                    : new CustomerRefundObservation(
                        request.RefundId,
                        request.OriginalPayment.PaymentReference,
                        request.Amount,
                        request.Destination,
                        CustomerRefundState.Sent,
                        "ref_fictional_driver",
                        new Clock().GetUtcNow(),
                        CustomerRefundEvidenceSource.FictionalGateway
                    )
            );
        }
    }

    [Theory]
    [InlineData(180, 120)]
    [InlineData(30, 30)]
    public async Task Gateway_operation_token_is_finite_and_capped_by_the_retained_deadline(
        int remaining,
        int budgetSeconds
    )
    {
        var b = CustomerRefundDecisionTests.Admitted();
        var c = b.CustomerRefund!;
        var clock = new BudgetClock(c.DeadlineAt.AddSeconds(-remaining));
        var gateway = (ICustomerRefundGateway)
            CustomerRefundOrchestrationTests.Proxy.Make(
                typeof(ICustomerRefundGateway),
                (m, a) =>
                {
                    if (m.Name != "ExecuteAsync")
                        throw new NotSupportedException(m.Name);
                    var ct = (CancellationToken)a![1]!;
                    ct.CanBeCanceled.ShouldBeTrue();
                    clock.Due.ShouldBe(TimeSpan.FromSeconds(budgetSeconds));
                    return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
                        Error.Failure("Fictional.Lost", "Fictional lost result.")
                    );
                }
            );
        await ExecuteCustomerRefundHandler.Handle(
            new(b.Id, c.Id),
            Session(b, [], () => { }),
            Store(b, [], () => { }),
            CustomerRefundOrchestrationTests.Outbox([]),
            gateway,
            new Sender(),
            clock,
            CancellationToken.None
        );
        b.CustomerRefund!.DeadlineAt.ShouldBe(c.DeadlineAt);
    }

    private sealed class BudgetClock(DateTimeOffset now) : TimeProvider
    {
        internal TimeSpan Due;

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            Due = dueTime;
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
