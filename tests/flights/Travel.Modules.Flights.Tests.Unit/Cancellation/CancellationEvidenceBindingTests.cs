using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationEvidenceBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preflight_order_fact_is_retained_even_before_confirmation_claim(bool accepted)
    {
        var booking = accepted
            ? CancellationDecisionTests.Prepared()
            : CancellationLifecycleTests.ClaimedPreparation();
        if (accepted)
            CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        var op = booking.CurrentCancellation!;
        var fact = new SupplierOrderCancellationFact(
            op.ProviderOrderRef,
            null,
            Now,
            Now,
            CancellationResolutionSource.SupplierApi
        );
        var result = booking.DecideCancellationExternalFact(op.Id, op.Revision, fact, Now);
        result.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        CancellationDecisionTests.Apply(booking, result);
        booking.Status.ShouldBe(BookingStatus.Cancelled);
        booking.CurrentCancellation!.Evidence.ShouldBeNull();
        booking.CurrentCancellation.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
    }

    private static readonly DateTimeOffset Now = CancellationTestData.Now;

    [Fact]
    public void Immediate_unmatched_confirmation_preserves_order_fact_and_requires_manual_review()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var fact = new SupplierOrderCancellationFact(
            op.ProviderOrderRef,
            "occ_other",
            Now.AddSeconds(1),
            Now.AddSeconds(2),
            CancellationResolutionSource.SupplierApi
        );
        var decision = booking.DecideCancellationExternalFact(
            op.Id,
            op.Revision,
            fact,
            Now.AddSeconds(2)
        );
        CancellationDecisionTests.Apply(booking, decision);
        booking.Status.ShouldBe(BookingStatus.Cancelled);
        booking.ObservedSupplierCancellation.ShouldBe(fact);
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        booking.CurrentCancellation.Outcome.ShouldBe(CancellationOutcome.Unknown);
        booking.CurrentCancellation.Evidence.ShouldBeNull();
        booking
            .DecideCancellationExternalFact(op.Id, op.Revision, fact, Now.AddSeconds(3))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Immediate_external_fact_rejects_foreign_order_or_invented_provenance()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        foreach (
            var fact in new[]
            {
                new SupplierOrderCancellationFact(
                    "ord_other",
                    null,
                    Now,
                    Now,
                    CancellationResolutionSource.SupplierApi
                ),
                new SupplierOrderCancellationFact(
                    op.ProviderOrderRef,
                    null,
                    Now,
                    Now,
                    CancellationResolutionSource.OperatorVerified
                ),
            }
        )
            booking
                .DecideCancellationExternalFact(op.Id, op.Revision, fact, Now)
                .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Delayed_preparation_read_is_history_only_after_confirmation_claim()
    {
        var booking = CancellationLifecycleTests.ClaimedPreparation();
        var op = booking.CurrentCancellation!;
        var read = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                0,
                read,
                Now.AddSeconds(2)
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationTerms(
                op.Id,
                op.Revision,
                CancellationTerms.Create(CancellationTestData.Input(), Now).Value,
                Now.AddSeconds(3)
            )
        );
        CancellationDecisionTests.Apply(
            booking,
            CancellationDecisionTests.Consent(booking, Now.AddSeconds(3))
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now.AddSeconds(3)
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObservation(
                op.Id,
                op.Revision,
                read,
                Pending(op, Now.AddSeconds(2)),
                Now.AddSeconds(4)
            )
        );
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.DispatchClaimed);
        booking.CurrentCancellation.HadUnknown.ShouldBeFalse();
    }

    [Fact]
    public void Unreserved_read_cannot_be_saved_as_supplier_api_evidence()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        booking
            .DecideCancellationObservation(
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                Pending(op, Now.AddSeconds(3)),
                Now.AddSeconds(3)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Recorded_read_retry_is_immutable_and_precedes_stale_revision_check()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var read = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                0,
                read,
                Now.AddSeconds(2)
            )
        );
        op = booking.CurrentCancellation!;
        var version = op.Revision;
        var observation = Pending(op, Now.AddSeconds(3));
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObservation(
                op.Id,
                version,
                read,
                observation,
                Now.AddSeconds(3)
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationObservation(op.Id, version, read, observation, Now.AddSeconds(4))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking
            .DecideCancellationObservation(
                op.Id,
                op.Revision,
                read,
                observation with
                {
                    Reason = CancellationReason.ProviderUnavailable,
                },
                Now.AddSeconds(4)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void One_accepted_refresh_remains_pending_across_cooldown()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var first = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                op.Revision,
                first,
                new string('a', 64),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                new string('b', 64),
                Now.AddSeconds(61)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        op.LastRefreshRequestId.ShouldBe(first);
    }

    [Fact]
    public void Busy_read_consumes_coalesced_on_demand_work_so_it_cannot_wake_later()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var request = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                op.Revision,
                request,
                new string('a', 64),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        var automatic = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                0,
                automatic,
                Now.AddSeconds(2)
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                -1,
                request,
                Now.AddSeconds(3)
            )
        );
        op = booking.CurrentCancellation!;
        op.ConsumedRefreshRequestId.ShouldBe(request);
        op.ActiveReadId.ShouldBe(automatic);
        booking
            .DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                -1,
                request,
                Now.AddSeconds(13)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Inconclusive_audit_cannot_store_unbound_unbounded_or_malformed_optional_fields(
        int kind
    )
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var evidence = new ManualResolutionEvidence(
            "SUPPORT-0001",
            ManualEvidenceCategory.Inconclusive,
            Now
        );
        evidence = kind switch
        {
            0 => evidence with { ProviderOrderRef = "ord_foreign" },
            1 => evidence with { SupplierReceiptRef = new string('x', 10000) },
            2 => evidence with { SupplierReceiptRef = "name\r\nfictional" },
            3 => evidence with { Destination = (CancellationRefundDestination)999 },
            4 => evidence with
            {
                StoppedInstanceIds = new EquatableArray<Guid>(
                    Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToArray()
                ),
            },
            5 => evidence with
            {
                Settlement = new(
                    (SettlementComposition)999,
                    false,
                    CancellationResolutionSource.SupplierApi,
                    false,
                    false
                ),
            },
            _ => evidence with { PaymentReference = default(PaymentRef) },
        };
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Cancellation,
            op.Id,
            op.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.RecordInconclusive,
            evidence
        );
        booking
            .DecideManualResolution(Guid.NewGuid(), input, Now)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Supplier_confirmation_matches_manual_prepared_economics_without_rewriting_either_source()
    {
        var input = CancellationTestData.Input();
        var settlement = input.Settlement with
        {
            Provenance = CancellationResolutionSource.OperatorVerified,
        };
        var terms = CancellationTerms.Create(input with { Settlement = settlement }, Now).Value;
        var facts = new CancellationConfirmationFacts(
            terms.ProviderOrderRef,
            terms.ProviderCancellationRef,
            terms.ItineraryPartyHash,
            terms.Refund,
            terms.Destination,
            input.Settlement,
            Now,
            Now,
            CancellationResolutionSource.SupplierApi
        );
        var evidence = CancellationEvidence.FromSupplierConfirmation(terms, facts, Now);
        evidence.IsError.ShouldBeFalse();
        evidence.Value.Settlement.Provenance.ShouldBe(CancellationResolutionSource.SupplierApi);
        terms.Settlement.Provenance.ShouldBe(CancellationResolutionSource.OperatorVerified);
    }

    [Fact]
    public void Resolved_preparation_uncertainty_does_not_turn_first_confirmation_refusal_into_unknown()
    {
        var booking = CancellationLifecycleTests.ClaimedPreparation();
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Preparation,
                CancellationReason.ProviderUnavailable,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationTerms(
                op.Id,
                op.Revision,
                CancellationTerms.Create(CancellationTestData.Input(), Now).Value,
                Now
            )
        );
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefusal(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                true,
                CancellationReason.StaleProposal,
                Now
            )
        );
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Rejected);
        booking.CurrentCancellation.HadUnknown.ShouldBeTrue();
    }

    [Fact]
    public void Actual_api_success_can_finish_consent_to_operator_prepared_terms()
    {
        var booking = CancellationLifecycleTests.ClaimedPreparation();
        var op = booking.CurrentCancellation!;
        var data = CancellationTestData.Input();
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Cancellation,
            op.Id,
            op.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.ConfirmPreparedTerms,
            new(
                "SUPPORT-0001",
                ManualEvidenceCategory.SupplierSupportAttestation,
                Now,
                ProviderOrderRef: op.ProviderOrderRef,
                ProviderCancellationRef: data.ProviderCancellationRef,
                Refund: data.Refund,
                Destination: data.Destination,
                Settlement: data.Settlement,
                ItineraryPartyHash: op.ItineraryPartyHash,
                TermsExpiresAt: data.ExpiresAt,
                PreparationCorrelationAttested: true
            )
        );
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideManualResolution(Guid.NewGuid(), input, Now)
        );
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        var terms = op.Terms!;
        var proof = CancellationEvidence.FromSupplierConfirmation(
            terms,
            new(
                terms.ProviderOrderRef,
                terms.ProviderCancellationRef,
                terms.ItineraryPartyHash,
                terms.Refund,
                terms.Destination,
                data.Settlement,
                Now,
                Now,
                CancellationResolutionSource.SupplierApi
            ),
            Now
        );
        proof.IsError.ShouldBeFalse();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationSuccess(op.Id, op.Revision, proof.Value, Now)
        );
        booking.CurrentCancellation!.ResolutionSource.ShouldBe(
            CancellationResolutionSource.SupplierApi
        );
        booking.CurrentCancellation.Terms!.Settlement.Provenance.ShouldBe(
            CancellationResolutionSource.OperatorVerified
        );
    }

    [Theory]
    [InlineData(CancellationResolutionSource.None)]
    [InlineData(CancellationResolutionSource.TravelAdmission)]
    [InlineData((CancellationResolutionSource)999)]
    public void Economic_match_without_valid_financial_provenance_is_not_evidence(
        CancellationResolutionSource source
    )
    {
        var terms = CancellationTerms.Create(CancellationTestData.Input(), Now).Value;
        var facts = new CancellationConfirmationFacts(
            terms.ProviderOrderRef,
            terms.ProviderCancellationRef,
            terms.ItineraryPartyHash,
            terms.Refund,
            terms.Destination,
            terms.Settlement with
            {
                Provenance = source,
            },
            Now,
            Now,
            CancellationResolutionSource.SupplierApi
        );
        CancellationEvidence.FromSupplierConfirmation(terms, facts, Now).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accepted_manual_receipt_retry_survives_later_different_authoritative_facts(
        bool confirmation
    )
    {
        var booking = confirmation
            ? ConfirmationBarrierTests.Claimed()
            : CancellationLifecycleTests.ClaimedPreparation();
        var actor = Guid.NewGuid();
        var target = confirmation
            ? booking.CurrentConfirmationAttempt!.Id
            : booking.CurrentCancellation!.Id;
        var version = confirmation
            ? booking.CurrentConfirmationAttempt!.Revision
            : booking.CurrentCancellation!.Revision;
        var evidence = new ManualResolutionEvidence(
            "SUPPORT-0001",
            ManualEvidenceCategory.Inconclusive,
            Now
        );
        evidence = confirmation
            ? evidence with
            {
                SupplierReceiptRef = "pay_fictional_candidate",
            }
            : evidence with
            {
                Refund = CancellationTestData.Money(50m),
            };
        var input = new ManualResolutionInput(
            booking.Id,
            confirmation
                ? ManualResolutionTargetKind.Confirmation
                : ManualResolutionTargetKind.Cancellation,
            target,
            version,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.RecordInconclusive,
            evidence
        );
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(actor, input, Now));
        if (confirmation)
        {
            var attempt = booking.CurrentConfirmationAttempt!;
            var sender = attempt.DispatchOwnerInstanceId!.Value;
            var payment = PaymentRef.New();
            CancellationDecisionTests.Apply(
                booking,
                booking.DecideConfirmationPaymentReference(
                    attempt.Id,
                    attempt.Revision,
                    sender,
                    payment,
                    Now
                )
            );
            attempt = booking.CurrentConfirmationAttempt!;
            CancellationDecisionTests.Apply(
                booking,
                booking.DecideConfirmationCapture(
                    attempt.Id,
                    attempt.Revision,
                    sender,
                    payment,
                    attempt.AcceptedMoney!,
                    Now
                )
            );
            attempt = booking.CurrentConfirmationAttempt!;
            CancellationDecisionTests.Apply(
                booking,
                booking.DecideConfirmationComplete(
                    attempt.Id,
                    attempt.Revision,
                    payment,
                    attempt.ProviderOrderRef,
                    SupplierPaymentEvidence
                        .Create(
                            "pay_fictional_actual",
                            attempt.AcceptedMoney!,
                            SupplierPaymentKind.Balance
                        )
                        .Value,
                    CancellationResolutionSource.SupplierApi,
                    Now
                )
            );
        }
        else
        {
            var op = booking.CurrentCancellation!;
            CancellationDecisionTests.Apply(
                booking,
                booking.DecideCancellationTerms(
                    op.Id,
                    op.Revision,
                    CancellationTerms.Create(CancellationTestData.Input(), Now).Value,
                    Now
                )
            );
        }
        booking
            .DecideManualResolution(actor, input, Now.AddSeconds(1))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking
            .DecideManualResolution(
                actor,
                input with
                {
                    Evidence = evidence with { EvidenceRef = "SUPPORT-0002" },
                },
                Now.AddSeconds(1)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void External_cancel_fact_changes_order_status_but_keeps_own_operation_unknown_manual()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var read = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                0,
                read,
                Now.AddSeconds(2)
            )
        );
        op = booking.CurrentCancellation!;
        var fact = new SupplierOrderCancellationFact(
            op.ProviderOrderRef,
            "occ_foreign",
            Now.AddSeconds(1),
            Now.AddSeconds(2),
            CancellationResolutionSource.SupplierApi
        );
        var observation = new CancellationObservation(
            op.ProviderOrderRef,
            op.ProviderCancellationRef,
            CancellationObservationState.Unknown,
            null,
            null,
            Now.AddSeconds(2),
            CancellationResolutionSource.SupplierApi,
            CancellationReason.IdentityMismatch,
            fact
        );
        var decision = booking.DecideCancellationObservation(
            op.Id,
            op.Revision,
            read,
            observation,
            Now.AddSeconds(2)
        );
        CancellationDecisionTests.Apply(booking, decision);
        booking.Status.ShouldBe(Travel.Modules.Flights.Core.Aggregates.BookingStatus.Cancelled);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Unknown);
        booking.CurrentCancellation.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        booking.CurrentCancellation.Evidence.ShouldBeNull();
        decision.Events.OfType<OrderRefunded>().ShouldBeEmpty();
        decision.Events.OfType<CancellationSucceeded>().ShouldBeEmpty();
    }

    private static CancellationObservation Pending(CancellationOperation op, DateTimeOffset at) =>
        new(
            op.ProviderOrderRef,
            op.ProviderCancellationRef,
            CancellationObservationState.Pending,
            null,
            null,
            at,
            CancellationResolutionSource.SupplierApi
        );
}
