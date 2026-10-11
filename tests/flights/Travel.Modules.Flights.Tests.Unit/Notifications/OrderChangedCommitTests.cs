using System.Reflection;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Tests.Unit.Cancellation;
using Travel.Modules.Flights.Tests.Unit.Refunds;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Notifications;

public sealed class OrderChangedCommitTests
{
    [Fact]
    public async Task Cancellation_notification_is_enrolled_before_save_with_the_preappend_target()
    {
        var booking = CancellationDecisionTests.Held();
        var decision = booking.DecideCancellationPrepare(
            booking.OwnerUserId!.Value,
            Guid.NewGuid(),
            booking.Version,
            new string('p', 64),
            Guid.NewGuid(),
            CancellationTestData.Now,
            new string('i', 64)
        );
        var appended = new List<object>();
        var messages = new List<(object Message, Wolverine.DeliveryOptions? Options)>();
        var target = booking.Version + decision.Events.Count;
        await Persist(
            decision,
            CustomerRefundOrchestrationTests.Stream(booking, appended),
            CustomerRefundOrchestrationTests.Session(() =>
            {
                booking.CurrentCancellation.ShouldBeNull();
                messages
                    .Select(m => m.Message)
                    .OfType<OrderChangedNotification>()
                    .ShouldHaveSingleItem()
                    .RequiredStreamVersion.ShouldBe(target);
            }),
            CustomerRefundOrchestrationTests.Outbox(messages)
        );
        appended.Count.ShouldBe(decision.Events.Count);
        booking.CurrentCancellation.ShouldNotBeNull();
    }

    [Fact]
    public async Task Noop_decision_does_not_append_save_or_enroll_any_notification()
    {
        var booking = CancellationDecisionTests.Held();
        var appended = new List<object>();
        var messages = new List<(object Message, Wolverine.DeliveryOptions? Options)>();
        await Persist(
            new(CancellationDecisionKind.NoOp, CancellationReason.None, []),
            CustomerRefundOrchestrationTests.Stream(booking, appended),
            CustomerRefundOrchestrationTests.Session(() =>
                throw new InvalidOperationException("No-op must not save")
            ),
            CustomerRefundOrchestrationTests.Outbox(messages)
        );
        appended.ShouldBeEmpty();
        messages.ShouldBeEmpty();
    }

    [Fact]
    public void First_hold_batch_is_eligible_while_source_is_still_quoted_and_EF_is_absent()
    {
        var (booking, _, now) = Ancillaries.AncillaryConfirmationTests.Held(false);
        booking.Status.ShouldBe(BookingStatus.OfferQuoted);
        IDomainEvent[] batch =
        [
            new OfferHeldV3(
                "ord_fictional",
                Travel
                    .Modules.Flights.Core.ValueObjects.ProtectedPassengerPartySnapshot.Create(
                        1,
                        "opaque-fictional"
                    )
                    .Value,
                now.AddMinutes(10),
                now,
                booking.OwnerUserId!.Value,
                booking.QuoteBinding!.Revision,
                2
            ),
        ];
        OrderChangedNotifications
            .For(booking, 12, batch)
            .ShouldHaveSingleItem()
            .ShouldBeOfType<OrderChangedNotification>()
            .RequiredStreamVersion.ShouldBe(12);
    }

    [Fact]
    public void Source_only_prehold_review_does_not_create_a_projection_dependent_notification()
    {
        var booking = new BookingAggregate();
        IDomainEvent[] batch = [new OrderCancelled(CancelReason.User, DateTimeOffset.UnixEpoch)];
        OrderChangedNotifications.For(booking, 10, batch).ShouldBeEmpty();
        OrderChangedNotifications.For(booking, 10, []).ShouldBeEmpty();
    }

    [Fact]
    public void An_owned_held_source_needs_no_EF_row_to_enroll_a_checkpoint_notification()
    {
        var owner = Guid.NewGuid();
        var booking = new BookingAggregate();
        booking.Apply(
            new OfferHeld(
                "ord_fictional",
                null!,
                DateTimeOffset.UnixEpoch.AddHours(1),
                DateTimeOffset.UnixEpoch,
                owner
            )
        );
        IDomainEvent[] batch = [new OrderCancelled(CancelReason.User, DateTimeOffset.UnixEpoch)];
        var notification = OrderChangedNotifications
            .For(booking, 11, batch)
            .ShouldHaveSingleItem()
            .ShouldBeOfType<OrderChangedNotification>();
        notification.UserId.ShouldBe(owner);
        notification.RequiredStreamVersion.ShouldBe(11);
    }

    // Same private-contract access pattern as CancellationSourceOwnershipTests.
    private static Task Persist(
        CancellationDecision decision,
        JasperFx.Events.IEventStream<BookingAggregate> stream,
        Marten.IDocumentSession session,
        Wolverine.Marten.IMartenOutbox outbox
    ) =>
        (Task)
            typeof(PrepareCancellationHandler)
                .Assembly.GetType(
                    "Travel.Modules.Flights.Application.Cancellation.CancellationDecisionWriter"
                )!
                .GetMethod("Persist", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(
                    null,
                    [
                        decision,
                        stream,
                        session,
                        outbox,
                        Array.Empty<BookingWork>(),
                        TestContext.Current.CancellationToken,
                    ]
                )!;
}
