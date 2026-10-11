using System.Reflection;
using Marten;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Wolverine;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundOrchestrationTests
{
    [Fact]
    public async Task Admission_work_and_fixed_deadline_are_saved_before_case_is_applied()
    {
        var b = RefundTestBookings.Cancelled();
        var d = b.DecideCustomerRefundAdmission(
            RefundTestBookings.Owner,
            RefundTestBookings.Cancellation,
            Guid.NewGuid(),
            RefundTestBookings.Now.AddSeconds(6)
        ).Value;
        var events = new List<object>();
        var work = new List<(object Message, DeliveryOptions? Options)>();
        var saved = false;
        var stream = Stream(b, events);
        var target = b.Version + d.Events.Count;
        await CustomerRefundWriter.Persist(
            d,
            stream,
            Session(() =>
            {
                b.CustomerRefund.ShouldBeNull();
                work.Select(x => x.Message)
                    .OfType<OrderChangedNotification>()
                    .ShouldHaveSingleItem()
                    .RequiredStreamVersion.ShouldBe(target);
                work.Select(x => x.Message).OfType<ExecuteCustomerRefund>().Count().ShouldBe(1);
                work.Single(x => x.Message is CustomerRefundDeadline)
                    .Options!.ScheduledTime.ShouldBe(RefundTestBookings.Now.AddSeconds(186));
                saved = true;
            }),
            Outbox(work),
            CancellationToken.None
        );
        saved.ShouldBeTrue();
        b.CustomerRefund.ShouldNotBeNull();
        events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_claim_commit_does_not_apply_dispatch_permission()
    {
        var b = CustomerRefundDecisionTests.Admitted();
        var c = b.CustomerRefund!;
        var d = b.DecideCustomerRefundClaim(
            c.Id,
            c.Revision,
            RefundTestBookings.Sender,
            RefundTestBookings.Now.AddSeconds(7)
        ).Value;
        await Should.ThrowAsync<InvalidOperationException>(() =>
            CustomerRefundWriter.Persist(
                d,
                Stream(b, []),
                Session(() => throw new InvalidOperationException("Synthetic commit failure")),
                Outbox([]),
                CancellationToken.None
            )
        );
        b.CustomerRefund!.ClaimedAt.ShouldBeNull();
        b.CanContinueCustomerRefund(c.Id, RefundTestBookings.Sender).ShouldBeFalse();
    }

    internal static JasperFx.Events.IEventStream<BookingAggregate> Stream(
        BookingAggregate b,
        List<object> events
    ) =>
        (JasperFx.Events.IEventStream<BookingAggregate>)
            Proxy.Make(
                typeof(JasperFx.Events.IEventStream<BookingAggregate>),
                (m, a) =>
                    m.Name switch
                    {
                        "get_Aggregate" => b,
                        "get_CurrentVersion" => (long?)b.Version,
                        "AppendOne" => Add(events, a![0]!),
                        _ => throw new NotSupportedException(m.Name),
                    }
            );

    internal static IDocumentSession Session(Action save) =>
        (IDocumentSession)
            Proxy.Make(
                typeof(IDocumentSession),
                (m, _) =>
                {
                    if (m.Name == "SaveChangesAsync")
                    {
                        save();
                        return Task.CompletedTask;
                    }
                    throw new NotSupportedException(m.Name);
                }
            );

    internal static IMartenOutbox Outbox(List<(object Message, DeliveryOptions? Options)> work) =>
        (IMartenOutbox)
            Proxy.Make(
                typeof(IMartenOutbox),
                (m, a) =>
                {
                    if (m.Name == "Enroll")
                        return null;
                    if (m.Name == "PublishAsync")
                        work.Add((a![0]!, a.Length > 1 ? a[1] as DeliveryOptions : null));
                    return m.ReturnType == typeof(ValueTask) ? ValueTask.CompletedTask
                        : m.ReturnType == typeof(Task) ? Task.CompletedTask
                        : null;
                }
            );

    private static object? Add(List<object> events, object e)
    {
        events.Add(e);
        return null;
    }

    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        public static object Make(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var p = (Proxy)Create(type, typeof(Proxy));
            p.Handler = handler;
            return p;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Handler(method!, args);
    }
}
