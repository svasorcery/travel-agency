using JasperFx.Events;
using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Refunds;

public static class CustomerRefundWriter
{
    public static async Task Persist(
        CustomerRefundDecision decision,
        IEventStream<BookingAggregate> stream,
        IDocumentSession session,
        IMartenOutbox outbox,
        CancellationToken ct
    )
    {
        if (decision.Events.Count == 0)
            return;
        var b = stream.Aggregate;
        if (b is null || !b.HasConsistentMutationOwner || b.Id == Guid.Empty)
            throw new BookingSourceOwnershipMissingException(
                "Customer refund source owner missing."
            );
        var work = new List<BookingWork>();
        foreach (var e in decision.Events)
        {
            if (
                e
                is not (
                    CustomerRefundRequested
                    or CustomerRefundDispatchClaimed
                    or CustomerRefundOutcomeRecorded
                    or CustomerRefundRequiredManualReview
                    or CustomerRefundWasNotRequired
                    or CustomerRefundReviewRecorded
                )
            )
                throw new InvalidOperationException("Customer refund event unsupported.");
            if (e is CustomerRefundRequested { Case.State: CustomerRefundState.Ready } created)
            {
                work.Add(new(new ExecuteCustomerRefund(b.Id, created.Case.Id), null));
                work.Add(
                    new(
                        new CustomerRefundDeadline(b.Id, created.Case.Id, created.Case.DeadlineAt),
                        created.Case.DeadlineAt
                    )
                );
            }
        }
        var notifications = OrderChangedNotifications.For(
            b,
            (stream.CurrentVersion ?? 0) + decision.Events.Count,
            decision.Events
        );
        foreach (var e in decision.Events)
            stream.AppendOne(e);
        await session.SaveBookingWithWorkAsync(outbox, b.Id, work, notifications, ct);
        foreach (var e in decision.Events)
        {
            switch (e)
            {
                case CustomerRefundRequested x:
                    b.Apply(x);
                    break;
                case CustomerRefundDispatchClaimed x:
                    b.Apply(x);
                    break;
                case CustomerRefundOutcomeRecorded x:
                    b.Apply(x);
                    break;
                case CustomerRefundRequiredManualReview x:
                    b.Apply(x);
                    break;
                case CustomerRefundWasNotRequired x:
                    b.Apply(x);
                    break;
                case CustomerRefundReviewRecorded x:
                    b.Apply(x);
                    break;
            }
        }
    }
}
