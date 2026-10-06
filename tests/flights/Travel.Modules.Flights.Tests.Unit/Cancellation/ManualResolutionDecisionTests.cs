using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class ManualResolutionDecisionTests
{
    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000901");
    private static readonly DateTimeOffset At = CancellationTestData.Now.AddSeconds(320);

    [Fact]
    public void Legacy_inconclusive_review_records_audit_without_enabling_writers_or_inventing_an_attempt()
    {
        var booking = CancellationDecisionTests.Held(false);
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.LegacyHeld,
            booking.Id,
            booking.Version,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.RecordInconclusive,
            new("EVIDENCE-LEGACY", ManualEvidenceCategory.Inconclusive, At)
        );
        var decision = booking.DecideManualResolution(Actor, input, At);
        decision.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        CancellationDecisionTests.Apply(booking, decision);
        booking.MutationCoordinationEnabled.ShouldBeFalse();
        booking.CurrentConfirmationAttempt.ShouldBeNull();
        booking.Status.ShouldBe(Travel.Modules.Flights.Core.Aggregates.BookingStatus.Held);
        booking.ManualReviewHistory.Count.ShouldBe(1);
        booking
            .DecideManualResolution(Actor, input, At.AddSeconds(1))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Support_attestation_can_confirm_exact_accepted_terms_only_as_operator_verified()
    {
        var booking = Manual();
        var input = ConfirmationInput(booking);
        var decision = booking.DecideManualResolution(Actor, input, At);
        CancellationDecisionTests.Apply(booking, decision);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Succeeded);
        booking.CurrentCancellation!.ResolutionSource.ShouldBe(
            CancellationResolutionSource.OperatorVerified
        );
    }

    [Fact]
    public void Reusing_resolution_id_returns_no_events_before_stale_revision_guard()
    {
        var booking = Manual();
        var input = ConfirmationInput(booking);
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(Actor, input, At));
        var retry = booking.DecideManualResolution(Actor, input, At.AddSeconds(1));
        retry.Kind.ShouldBe(CancellationDecisionKind.NoOp);
        retry.Events.Count.ShouldBe(0);
        booking
            .DecideManualResolution(
                Actor,
                input with
                {
                    Evidence = input.Evidence with { EvidenceRef = "SUPPORT-0002" },
                },
                At.AddSeconds(1)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Pending_or_empty_support_fact_is_not_positive_no_effect_proof()
    {
        var booking = Manual();
        var input = ConfirmationInput(booking) with
        {
            Decision = ManualResolutionDecisionKind.ConfirmNoEffect,
        };
        booking
            .DecideManualResolution(Actor, input, At)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Negative_after_dispatch_requires_sender_quiescence_and_fresh_positive_final_absence()
    {
        var booking = Manual();
        var op = booking.CurrentCancellation!;
        var input = ConfirmationInput(booking) with
        {
            Decision = ManualResolutionDecisionKind.ConfirmNoEffect,
        };
        input = input with
        {
            Evidence = input.Evidence with
            {
                Category = ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation,
                SupplierFinalNoEffectsConfirmed = true,
                SenderEgressIsolated = true,
                OldInstancesCannotResume = true,
                StoppedAt = At.AddSeconds(-2),
                QuiescenceRef = "QUIESCENCE-0001",
                StoppedInstanceIds = new EquatableArray<Guid>(
                    new[] { op.DispatchOwnerInstanceId!.Value }
                ),
            },
        };
        booking
            .DecideManualResolution(
                Actor,
                input with
                {
                    Evidence = input.Evidence with
                    {
                        StoppedInstanceIds = new EquatableArray<Guid>(new[] { Guid.NewGuid() }),
                    },
                },
                At
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(Actor, input, At));
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Rejected);
        booking.CurrentCancellation!.ResolutionSource.ShouldBe(
            CancellationResolutionSource.OperatorVerified
        );
    }

    [Fact]
    public void Nested_api_provenance_cannot_manufacture_a_stored_api_observation()
    {
        var booking = Manual();
        var input = ConfirmationInput(booking);
        input = input with
        {
            Evidence = input.Evidence with
            {
                Category = ManualEvidenceCategory.StoredSupplierObservation,
                EvidenceRef = "API-UNKNOWN1",
            },
        };
        booking
            .DecideManualResolution(Actor, input, At)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Evidence_reference_cannot_carry_url_or_unbounded_free_text()
    {
        var booking = Manual();
        var input = ConfirmationInput(booking);
        booking
            .DecideManualResolution(
                Actor,
                input with
                {
                    Evidence = input.Evidence with
                    {
                        EvidenceRef = "https://fictional.invalid/person",
                    },
                },
                At
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        booking
            .DecideManualResolution(Guid.Empty, input, At)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Authoritative_unclaimed_confirmation_can_close_without_external_no_effect_guess()
    {
        var booking = ConfirmationBarrierTests.Started();
        var attempt = booking.CurrentConfirmationAttempt!;
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Confirmation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.CloseNotDispatched,
            new("LOCAL-000001", ManualEvidenceCategory.Inconclusive, At)
        );
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(Actor, input, At));
        booking.HasConfirmationBarrier.ShouldBeFalse();
        booking.Status.ShouldBe(Travel.Modules.Flights.Core.Aggregates.BookingStatus.Held);
    }

    [Fact]
    public void Claimed_confirmation_does_not_close_from_absence_of_in_memory_wallet_state()
    {
        var booking = ConfirmationBarrierTests.Claimed();
        var attempt = booking.CurrentConfirmationAttempt!;
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Confirmation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.CloseNotDispatched,
            new("LOCAL-000001", ManualEvidenceCategory.Inconclusive, At)
        );
        booking
            .DecideManualResolution(Actor, input, At)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Legacy_coordination_requires_full_legacy_writer_drain_and_both_no_effect_facts()
    {
        var booking = CancellationDecisionTests.Held(false);
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.LegacyHeld,
            booking.Id,
            booking.Version,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.EnableLegacyCoordination,
            new("LEGACY-0001", ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation, At)
        );
        booking
            .DecideManualResolution(Actor, input, At)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        input = input with
        {
            Evidence = input.Evidence with
            {
                LegacyMutationFleetDrained = true,
                SenderEgressIsolated = true,
                OldInstancesCannotResume = true,
                SupplierFinalNoEffectsConfirmed = true,
                WalletFinalNoEffectsConfirmed = true,
                StoppedAt = At.AddSeconds(-1),
                QuiescenceRef = "QUIESCENCE-0001",
            },
        };
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(Actor, input, At));
        booking.MutationCoordinationEnabled.ShouldBeTrue();
        booking.Status.ShouldBe(Travel.Modules.Flights.Core.Aggregates.BookingStatus.Held);
    }

    [Fact]
    public void Manual_booking_requires_saved_wallet_reference_both_positive_facts_and_sender_stop()
    {
        var booking = ConfirmationBarrierTests.Claimed();
        var attempt = booking.CurrentConfirmationAttempt!;
        var payment = Travel.Modules.Flights.Core.ValueObjects.Identifiers.PaymentRef.New();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideConfirmationPaymentReference(
                attempt.Id,
                attempt.Revision,
                attempt.DispatchOwnerInstanceId!.Value,
                payment,
                CancellationTestData.Now
            )
        );
        attempt = booking.CurrentConfirmationAttempt!;
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Confirmation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.ConfirmBooking,
            new(
                "PAYMENT-0001",
                ManualEvidenceCategory.PaymentAndSupplierAttestation,
                At,
                ProviderOrderRef: attempt.ProviderOrderRef,
                StoppedAt: At.AddSeconds(-2),
                QuiescenceRef: "QUIESCENCE-0001",
                StoppedInstanceIds: new EquatableArray<Guid>(
                    new[] { attempt.DispatchOwnerInstanceId!.Value }
                ),
                SenderEgressIsolated: true,
                OldInstancesCannotResume: true,
                AcceptedMoney: CancellationTestData.Money(),
                PaymentReference: payment,
                SupplierReceiptRef: "pay_fictional_support_1",
                WalletCaptureConfirmed: true,
                SupplierBookingPaymentConfirmed: true,
                PaymentCorrelationAttested: true
            )
        );
        booking
            .DecideManualResolution(
                Actor,
                input with
                {
                    Evidence = input.Evidence with { WalletCaptureConfirmed = false },
                },
                At
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(Actor, input, At));
        booking.Status.ShouldBe(Travel.Modules.Flights.Core.Aggregates.BookingStatus.Confirmed);
        booking.CurrentConfirmationAttempt!.ResolutionSource.ShouldBe(
            CancellationResolutionSource.OperatorVerified
        );
    }

    [Fact]
    public void Operator_cannot_invent_missing_saved_wallet_reference()
    {
        var booking = ConfirmationBarrierTests.Claimed();
        var attempt = booking.CurrentConfirmationAttempt!;
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Confirmation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.ConfirmBooking,
            new(
                "PAYMENT-0001",
                ManualEvidenceCategory.PaymentAndSupplierAttestation,
                At,
                ProviderOrderRef: attempt.ProviderOrderRef,
                AcceptedMoney: CancellationTestData.Money(),
                PaymentReference: Travel.Modules.Flights.Core.ValueObjects.Identifiers.PaymentRef.New(),
                SupplierReceiptRef: "pay_fictional_support_1",
                WalletCaptureConfirmed: true,
                SupplierBookingPaymentConfirmed: true,
                PaymentCorrelationAttested: true
            )
        );
        booking
            .DecideManualResolution(Actor, input, At)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    private static Travel.Modules.Flights.Core.Aggregates.BookingAggregate Manual()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRecoveryDeadline(op.Id, op.Recovery!.Epoch, At)
        );
        return booking;
    }

    private static ManualResolutionInput ConfirmationInput(
        Travel.Modules.Flights.Core.Aggregates.BookingAggregate booking
    )
    {
        var op = booking.CurrentCancellation!;
        var terms = op.Terms!;
        return new(
            booking.Id,
            ManualResolutionTargetKind.Cancellation,
            op.Id,
            op.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.ConfirmCancellation,
            new ManualResolutionEvidence(
                "SUPPORT-0001",
                ManualEvidenceCategory.SupplierSupportAttestation,
                At,
                op.ProviderOrderRef,
                terms.ProviderCancellationRef,
                terms.Refund,
                terms.Destination,
                terms.Settlement,
                op.ItineraryPartyHash,
                CancellationTestData.Now.AddSeconds(1)
            )
        );
    }
}
