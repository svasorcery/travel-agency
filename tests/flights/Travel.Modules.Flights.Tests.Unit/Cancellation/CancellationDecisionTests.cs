using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationDecisionTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;
    private static readonly Guid Admission = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid Sender = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid Epoch = Guid.Parse("00000000-0000-0000-0000-000000000203");
    private static readonly Guid ConfirmAdmission = Guid.Parse(
        "00000000-0000-0000-0000-000000000204"
    );
    private static readonly string PrepareFingerprint = new('c', 64);
    private static readonly string ConsentFingerprint = new('d', 64);

    [Fact]
    public void Owner_and_legacy_guards_precede_supplier_admission()
    {
        var input = CancellationTestData.Input();
        var legacy = Held(false);
        Prepare(legacy, input.OwnerId).Kind.ShouldBe(CancellationDecisionKind.Rejected);
        var booking = Held();
        Prepare(booking, Guid.NewGuid()).Kind.ShouldBe(CancellationDecisionKind.Rejected);
        Prepare(booking, input.OwnerId).Kind.ShouldBe(CancellationDecisionKind.Allowed);
    }

    [Fact]
    public void Exact_prepare_retry_survives_stream_progress_without_repeating_events()
    {
        var booking = Held();
        var version = booking.Version;
        Apply(booking, Prepare(booking, CancellationTestData.Input().OwnerId));
        var repeat = booking.DecideCancellationPrepare(
            CancellationTestData.Input().OwnerId,
            CancellationTestData.Input().OperationId,
            version,
            PrepareFingerprint,
            Admission,
            Now,
            CancellationTestData.Input().ItineraryPartyHash
        );
        repeat.Kind.ShouldBe(CancellationDecisionKind.NoOp);
        repeat.Events.Count.ShouldBe(0);
        booking
            .DecideCancellationPrepare(
                CancellationTestData.Input().OwnerId,
                CancellationTestData.Input().OperationId,
                booking.Version,
                new string('e', 64),
                Admission,
                Now,
                CancellationTestData.Input().ItineraryPartyHash
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Another_operation_cannot_bypass_an_active_booking_gate()
    {
        var booking = Held();
        Apply(booking, Prepare(booking, CancellationTestData.Input().OwnerId));
        booking
            .DecideCancellationPrepare(
                CancellationTestData.Input().OwnerId,
                Guid.NewGuid(),
                booking.Version,
                PrepareFingerprint,
                Guid.NewGuid(),
                Now,
                CancellationTestData.Input().ItineraryPartyHash
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Terms_require_explicit_matching_consent_before_dispatch()
    {
        var booking = Prepared();
        var operation = booking.CurrentCancellation!;
        booking
            .DecideCancellationConfirmationDispatch(
                operation.Id,
                operation.Revision,
                ConfirmAdmission,
                Sender,
                Guid.NewGuid(),
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        var accepted = Consent(booking);
        accepted.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        Apply(booking, accepted);
        operation = booking.CurrentCancellation!;
        var dispatch = booking.DecideCancellationConfirmationDispatch(
            operation.Id,
            operation.Revision,
            ConfirmAdmission,
            Sender,
            Guid.NewGuid(),
            Now
        );
        dispatch.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        Apply(booking, dispatch);
        operation = booking.CurrentCancellation!;
        booking
            .DecideCancellationConfirmationDispatch(
                operation.Id,
                operation.Revision,
                ConfirmAdmission,
                Sender,
                Guid.NewGuid(),
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Changed_or_expired_terms_do_not_inherit_consent()
    {
        var booking = Prepared();
        var op = booking.CurrentCancellation!;
        booking
            .DecideCancellationConsent(
                CancellationTestData.Input().OwnerId,
                op.Id,
                op.Revision,
                op.Terms!.Revision,
                new string('f', 64),
                op.Terms.NoticeVersion,
                ConsentFingerprint,
                ConfirmAdmission,
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        Consent(booking, Now.AddHours(1)).Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Consent_and_abandon_are_exclusive_under_the_same_revision()
    {
        var booking = Prepared();
        Apply(booking, Consent(booking));
        var op = booking.CurrentCancellation!;
        booking
            .DecideCancellationAbandon(
                CancellationTestData.Input().OwnerId,
                op.Id,
                op.Revision,
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Possible_dispatch_remains_blocked_and_is_never_reissued_after_unknown()
    {
        var booking = Prepared();
        Apply(booking, Consent(booking));
        var op = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                ConfirmAdmission,
                Sender,
                Guid.NewGuid(),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                CancellationReason.ProviderUnavailable,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                ConfirmAdmission,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now.AddMinutes(20)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Unknown);
    }

    [Fact]
    public void Late_matching_success_survives_ticketing_and_does_not_create_customer_refund()
    {
        var booking = Prepared();
        Apply(booking, Consent(booking));
        var op = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                ConfirmAdmission,
                Sender,
                Guid.NewGuid(),
                Now
            )
        );
        booking.Apply(new OrderTicketed(new(new[] { "fictional-ticket" }), Now));
        op = booking.CurrentCancellation!;
        var facts = CancellationTestData.Confirmation(op.Terms!);
        var evidence = CancellationEvidence
            .FromSupplierConfirmation(op.Terms!, facts, facts.ObservedAt)
            .Value;
        var done = booking.DecideCancellationSuccess(
            op.Id,
            op.Revision,
            evidence,
            facts.ObservedAt
        );
        done.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        done.Events.Any(e => e is OrderRefunded).ShouldBeFalse();
        Apply(booking, done);
        booking.Status.ShouldBe(BookingStatus.Cancelled);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Succeeded);
    }

    [Fact]
    public void Old_operation_and_receipts_remain_readable_after_safe_replacement()
    {
        var booking = Prepared();
        var old = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationAbandon(
                CancellationTestData.Input().OwnerId,
                old.Id,
                old.Revision,
                Now
            )
        );
        var replacementId = Guid.NewGuid();
        Apply(
            booking,
            booking.DecideCancellationPrepare(
                CancellationTestData.Input().OwnerId,
                replacementId,
                booking.Version,
                new string('e', 64),
                Guid.NewGuid(),
                Now,
                CancellationTestData.Input().ItineraryPartyHash
            )
        );
        booking.CancellationOperations[old.Id].Phase.ShouldBe(CancellationPhase.Abandoned);
        booking.CurrentCancellation!.Id.ShouldBe(replacementId);
    }

    [Fact]
    public void Late_preparation_quote_cannot_reopen_confirmed_rejection()
    {
        var booking = Held();
        Apply(booking, Prepare(booking, CancellationTestData.Input().OwnerId));
        var op = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationPreparationDispatch(
                op.Id,
                op.Revision,
                Admission,
                Sender,
                Epoch,
                Now
            )
        );
        booking.Apply(
            new CancellationRejected(
                op.Id,
                CancellationUnknownStage.Preparation,
                CancellationResolutionSource.SupplierApi,
                CancellationReason.NotCancellable,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationTerms(
                op.Id,
                op.Revision,
                CancellationTerms.Create(CancellationTestData.Input(), Now).Value,
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.Rejected);
    }

    [Fact]
    public void Late_inconclusive_error_cannot_remove_explicit_manual_escalation()
    {
        var booking = Prepared();
        Apply(booking, Consent(booking));
        var op = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                ConfirmAdmission,
                Sender,
                Guid.NewGuid(),
                Now
            )
        );
        booking.Apply(
            new CancellationManualReviewRequired(
                op.Id,
                CancellationUnknownStage.Confirmation,
                CancellationReason.ManualVerificationRequired,
                false,
                Now.AddSeconds(310)
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                CancellationReason.ProviderUnavailable,
                Now.AddSeconds(311)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Terminal_booking_does_not_admit_new_consent_or_dispatch(bool refunded)
    {
        var ready = Prepared();
        MakeTerminal(ready, refunded);
        Consent(ready).Kind.ShouldBe(CancellationDecisionKind.Rejected);
        var accepted = Prepared();
        Apply(accepted, Consent(accepted));
        MakeTerminal(accepted, refunded);
        var op = accepted.CurrentCancellation!;
        var result = accepted.DecideCancellationConfirmationDispatch(
            op.Id,
            op.Revision,
            ConfirmAdmission,
            Sender,
            Guid.NewGuid(),
            Now
        );
        result.Kind.ShouldBe(CancellationDecisionKind.Rejected);
        result.Events.Count.ShouldBe(0);
    }

    private static void MakeTerminal(BookingAggregate booking, bool refunded)
    {
        if (refunded)
            booking.Apply(
                new OrderRefunded(
                    Travel.Modules.Flights.Core.ValueObjects.Identifiers.RefundRef.New(),
                    CancellationTestData.Money(),
                    RefundInitiator.Airline,
                    Now
                )
            );
        else
            booking.Apply(new OrderCancelled(CancelReason.Airline, Now));
    }

    internal static BookingAggregate Held(bool coordinated = true)
    {
        var input = CancellationTestData.Input();
        var booking = new BookingAggregate();
        // Marten supplies stream identity/version; this pure fixture mirrors only that metadata.
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Id))!
            .SetValue(booking, input.AggregateId);
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Version))!
            .SetValue(booking, 1);
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
        if (coordinated)
            booking.Apply(new BookingMutationCoordinationEnabled(Now));
        return booking;
    }

    private static CancellationDecision Prepare(BookingAggregate booking, Guid owner) =>
        booking.DecideCancellationPrepare(
            owner,
            CancellationTestData.Input().OperationId,
            booking.Version,
            PrepareFingerprint,
            Admission,
            Now,
            CancellationTestData.Input().ItineraryPartyHash
        );

    internal static BookingAggregate Prepared()
    {
        var booking = Held();
        Apply(booking, Prepare(booking, CancellationTestData.Input().OwnerId));
        var op = booking.CurrentCancellation!;
        Apply(
            booking,
            booking.DecideCancellationPreparationDispatch(
                op.Id,
                op.Revision,
                Admission,
                Sender,
                Epoch,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        var terms = CancellationTerms.Create(CancellationTestData.Input(), Now).Value;
        Apply(booking, booking.DecideCancellationTerms(op.Id, op.Revision, terms, Now));
        return booking;
    }

    internal static CancellationDecision Consent(
        BookingAggregate booking,
        DateTimeOffset? at = null
    )
    {
        var op = booking.CurrentCancellation!;
        return booking.DecideCancellationConsent(
            CancellationTestData.Input().OwnerId,
            op.Id,
            op.Revision,
            op.Terms!.Revision,
            op.Terms.Hash,
            op.Terms.NoticeVersion,
            ConsentFingerprint,
            ConfirmAdmission,
            at ?? Now
        );
    }

    internal static void Apply(BookingAggregate booking, CancellationDecision decision)
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
