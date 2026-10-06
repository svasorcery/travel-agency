using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class ManualPreparationResolutionTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Lost_creation_requires_exact_correlated_preparation_and_new_consent()
    {
        var booking = CancellationLifecycleTests.ClaimedPreparation();
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRecoveryDeadline(
                op.Id,
                op.Recovery!.Epoch,
                Now.AddSeconds(310)
            )
        );
        op = booking.CurrentCancellation!;
        var original = CancellationTestData.Input();
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
                Now.AddSeconds(320),
                ProviderOrderRef: op.ProviderOrderRef,
                ProviderCancellationRef: original.ProviderCancellationRef,
                Refund: original.Refund,
                Destination: original.Destination,
                Settlement: original.Settlement,
                ItineraryPartyHash: op.ItineraryPartyHash,
                TermsExpiresAt: Now.AddHours(2),
                PreparationCorrelationAttested: true
            )
        );
        booking
            .DecideManualResolution(
                Actor,
                input with
                {
                    Evidence = input.Evidence with { PreparationCorrelationAttested = false },
                },
                Now.AddSeconds(320)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideManualResolution(Actor, input, Now.AddSeconds(320))
        );
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.TermsReady);
        booking.CurrentCancellation.AcceptedAt.ShouldBeNull();
        booking.CurrentCancellation.Terms!.Settlement.Provenance.ShouldBe(
            CancellationResolutionSource.OperatorVerified
        );
        var ownerView =
            Travel.Modules.Flights.Application.Cancellation.CancellationStatusFactory.Create(
                booking,
                booking.Version,
                null,
                Now.AddSeconds(320)
            );
        ownerView.Operation!.Terms!.FinancialSource.ShouldBe("OperatorVerified");
        var accepted = booking.DecideCancellationConsent(
            original.OwnerId,
            op.Id,
            booking.CurrentCancellation!.Revision,
            booking.CurrentCancellation.Terms.Revision,
            booking.CurrentCancellation.Terms.Hash,
            "cancellation-v1",
            new string('c', 64),
            Guid.NewGuid(),
            Now.AddSeconds(320)
        );
        CancellationDecisionTests.Apply(booking, accepted);
        var current = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                current.Id,
                current.Revision,
                current.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now.AddSeconds(320)
            )
        );
        current = booking.CurrentCancellation!;
        var terms = current.Terms!;
        var proof = CancellationEvidence
            .FromSupplierConfirmation(
                terms,
                new(
                    terms.ProviderOrderRef,
                    terms.ProviderCancellationRef,
                    terms.ItineraryPartyHash,
                    terms.Refund,
                    terms.Destination,
                    terms.Settlement with
                    {
                        Provenance = CancellationResolutionSource.SupplierApi,
                    },
                    Now.AddSeconds(321),
                    Now.AddSeconds(322),
                    CancellationResolutionSource.SupplierApi
                ),
                Now.AddSeconds(322)
            )
            .Value;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationSuccess(
                current.Id,
                current.Revision,
                proof,
                Now.AddSeconds(322)
            )
        );
        ownerView =
            Travel.Modules.Flights.Application.Cancellation.CancellationStatusFactory.Create(
                booking,
                booking.Version,
                null,
                Now.AddSeconds(322)
            );
        ownerView.Operation!.ResolutionSource.ShouldBe("SupplierApi");
        ownerView.Operation.Terms!.FinancialSource.ShouldBe("OperatorVerified");
        booking
            .DecideManualResolution(Actor, input, Now.AddSeconds(321))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_not_dispatched_fences_late_worker_for_both_admissions(bool consent)
    {
        var booking = consent
            ? CancellationDecisionTests.Prepared()
            : CancellationDecisionTests.Held();
        var data = CancellationTestData.Input();
        if (consent)
            CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        else
            CancellationDecisionTests.Apply(
                booking,
                booking.DecideCancellationPrepare(
                    data.OwnerId,
                    data.OperationId,
                    booking.Version,
                    new string('a', 64),
                    Guid.NewGuid(),
                    Now,
                    data.ItineraryPartyHash
                )
            );
        var op = booking.CurrentCancellation!;
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Cancellation,
            op.Id,
            op.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.CloseNotDispatched,
            new("LOCAL-000001", ManualEvidenceCategory.Inconclusive, Now)
        );
        CancellationDecisionTests.Apply(booking, booking.DecideManualResolution(Actor, input, Now));
        op = booking.CurrentCancellation!;
        op.IsTerminal.ShouldBeTrue();
        op.Outcome.ShouldBe(CancellationOutcome.None);
        var late = consent
            ? booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
            : booking.DecideCancellationPreparationDispatch(
                op.Id,
                op.Revision,
                op.PreparationAdmissionId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            );
        late.Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Local_close_cannot_erase_possible_creation()
    {
        var booking = CancellationLifecycleTests.ClaimedPreparation();
        var op = booking.CurrentCancellation!;
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Cancellation,
            op.Id,
            op.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.CloseNotDispatched,
            new("LOCAL-000001", ManualEvidenceCategory.Inconclusive, Now)
        );
        booking
            .DecideManualResolution(Actor, input, Now)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }
}
