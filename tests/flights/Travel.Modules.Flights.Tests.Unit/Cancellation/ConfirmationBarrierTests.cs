using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class ConfirmationBarrierTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;
    private static readonly Guid Attempt = Guid.Parse("00000000-0000-0000-0000-000000000301");
    private static readonly Guid Admission = Guid.Parse("00000000-0000-0000-0000-000000000302");
    private static readonly Guid Sender = Guid.Parse("00000000-0000-0000-0000-000000000303");
    private static readonly string Fingerprint = new('e', 64);

    [Fact]
    public void Confirmation_admission_blocks_cancellation_without_a_cancellation_operation()
    {
        var booking = Started();
        booking.CurrentCancellation.ShouldBeNull();
        booking.HasConfirmationBarrier.ShouldBeTrue();
        var input = CancellationTestData.Input();
        booking
            .DecideCancellationPrepare(
                input.OwnerId,
                input.OperationId,
                booking.Version,
                new string('c', 64),
                Guid.NewGuid(),
                Now,
                input.ItineraryPartyHash
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Side_effect_claim_is_not_issued_twice_after_delivery_or_restart()
    {
        var booking = Claimed();
        var attempt = booking.CurrentConfirmationAttempt!;
        booking
            .DecideConfirmationEffectsClaim(
                attempt.Id,
                attempt.Revision,
                Admission,
                Guid.NewGuid(),
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Unclaimed_deadline_closes_only_its_immutable_admission()
    {
        var booking = Started();
        booking
            .DecideConfirmationDeadline(Attempt, Guid.NewGuid(), Now.AddSeconds(310))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        Apply(booking, booking.DecideConfirmationDeadline(Attempt, Admission, Now.AddSeconds(310)));
        booking.HasConfirmationBarrier.ShouldBeFalse();
        booking.Status.ShouldBe(BookingStatus.Held);
    }

    [Fact]
    public void Claimed_deadline_keeps_manual_barrier_without_fabricated_payment()
    {
        var booking = Claimed();
        Apply(booking, booking.DecideConfirmationDeadline(Attempt, Admission, Now.AddSeconds(310)));
        booking.HasConfirmationBarrier.ShouldBeTrue();
        booking.CurrentConfirmationAttempt!.Phase.ShouldBe(
            ConfirmationAttemptPhase.ManualReviewRequired
        );
        booking.PaymentRef.ShouldBeNull();
        booking.Status.ShouldBe(BookingStatus.Held);
    }

    [Fact]
    public void Capture_and_supplier_receipt_bind_to_saved_payment_and_accepted_amount()
    {
        var booking = Claimed();
        var attempt = booking.CurrentConfirmationAttempt!;
        var payment = PaymentRef.New();
        Apply(
            booking,
            booking.DecideConfirmationPaymentReference(
                Attempt,
                attempt.Revision,
                Sender,
                payment,
                Now
            )
        );
        attempt = booking.CurrentConfirmationAttempt!;
        booking
            .DecideConfirmationCapture(
                Attempt,
                attempt.Revision,
                Sender,
                PaymentRef.New(),
                CancellationTestData.Money(),
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        Apply(
            booking,
            booking.DecideConfirmationCapture(
                Attempt,
                attempt.Revision,
                Sender,
                payment,
                CancellationTestData.Money(),
                Now
            )
        );
        attempt = booking.CurrentConfirmationAttempt!;
        var receipt = SupplierPaymentEvidence
            .Create("pay_fictional_1", CancellationTestData.Money(), SupplierPaymentKind.Balance)
            .Value;
        Apply(
            booking,
            booking.DecideConfirmationComplete(
                Attempt,
                attempt.Revision,
                payment,
                CancellationTestData.Input().ProviderOrderRef,
                receipt,
                CancellationResolutionSource.SupplierApi,
                Now
            )
        );
        booking.Status.ShouldBe(BookingStatus.Confirmed);
        booking.HasConfirmationBarrier.ShouldBeFalse();
        booking.PaymentRef.ShouldBe(payment);
    }

    [Fact]
    public void Terminal_order_cannot_get_a_new_confirmation_claim()
    {
        var booking = Started();
        booking.Apply(new OrderCancelled(CancelReason.Airline, Now));
        var attempt = booking.CurrentConfirmationAttempt!;
        booking
            .DecideConfirmationEffectsClaim(Attempt, attempt.Revision, Admission, Sender, Now)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Readonly_preflight_failure_closes_only_unclaimed_attempt_without_payment_events()
    {
        var booking = Started();
        var attempt = booking.CurrentConfirmationAttempt!;
        var decision = booking.DecideConfirmationNotDispatched(
            attempt.Id,
            attempt.Revision,
            CancellationReason.ProviderUnavailable,
            CancellationTestData.Now
        );
        CancellationDecisionTests.Apply(booking, decision);
        booking.HasConfirmationBarrier.ShouldBeFalse();
        decision.Events.OfType<PaymentAuthorized>().ShouldBeEmpty();
        decision.Events.OfType<OrderConfirmed>().ShouldBeEmpty();
    }

    [Fact]
    public void Sender_must_recheck_persisted_attempt_before_every_next_effect()
    {
        var booking = Claimed();
        var attempt = booking.CurrentConfirmationAttempt!;
        var sender = attempt.DispatchOwnerInstanceId!.Value;
        booking.CanContinueConfirmation(attempt.Id, sender).ShouldBeTrue();
        booking.CanContinueConfirmation(attempt.Id, Guid.NewGuid()).ShouldBeFalse();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideConfirmationManual(
                attempt.Id,
                attempt.Revision,
                CancellationReason.ProviderUnavailable,
                CancellationTestData.Now
            )
        );
        booking.CanContinueConfirmation(attempt.Id, sender).ShouldBeFalse();
        booking
            .DecideConfirmationNotDispatched(
                attempt.Id,
                booking.CurrentConfirmationAttempt!.Revision,
                CancellationReason.ProviderUnavailable,
                CancellationTestData.Now
            )
            .Events.ShouldBeEmpty();
    }

    internal static BookingAggregate Started()
    {
        var input = CancellationTestData.Input();
        var booking = new BookingAggregate();
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Id))!
            .SetValue(booking, input.AggregateId);
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Version))!
            .SetValue(booking, 1);
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(2),
                "XX",
                "001",
                CabinClass.Economy
            )
            .Value;
        booking.Apply(
            new OfferQuoted(
                OfferId.New(),
                Itinerary.Create(new[] { Slice.Create(new[] { segment }).Value }).Value,
                CancellationTestData.Money(),
                Now.AddDays(1),
                "off_fictional_1",
                Now
            )
        );
        booking.Apply(
            new OfferHeldV3(
                input.ProviderOrderRef,
                ProtectedPassengerPartySnapshot.Create(1, "fictional-protected-party").Value,
                Now.AddDays(1),
                Now,
                input.OwnerId,
                Guid.NewGuid(),
                1
            )
        );
        booking.Apply(new BookingMutationCoordinationEnabled(Now));
        Apply(
            booking,
            booking.DecideConfirmationStart(
                input.OwnerId,
                Attempt,
                Admission,
                booking.Version,
                Fingerprint,
                Now
            )
        );
        return booking;
    }

    internal static BookingAggregate Claimed()
    {
        var booking = Started();
        Apply(
            booking,
            booking.DecideConfirmationEffectsClaim(
                Attempt,
                booking.CurrentConfirmationAttempt!.Revision,
                Admission,
                Sender,
                Now
            )
        );
        return booking;
    }

    private static void Apply(BookingAggregate booking, CancellationDecision decision)
    {
        decision.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        foreach (var e in decision.Events)
        {
            typeof(BookingAggregate)
                .GetMethod("Apply", new[] { e.GetType() })!
                .Invoke(booking, new object[] { e });
            typeof(BookingAggregate)
                .GetProperty(nameof(BookingAggregate.Version))!
                .SetValue(booking, booking.Version + 1);
        }
    }
}
