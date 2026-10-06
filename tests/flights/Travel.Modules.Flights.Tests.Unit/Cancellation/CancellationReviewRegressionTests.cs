using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationReviewRegressionTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;

    [Fact]
    public void Known_wallet_capture_cannot_be_erased_by_negative_operator_attestation()
    {
        var booking = ConfirmationBarrierTests.Claimed();
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
                CancellationTestData.Money(),
                Now
            )
        );
        attempt = booking.CurrentConfirmationAttempt!;
        var at = Now.AddSeconds(320);
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Confirmation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.ConfirmNoEffect,
            new(
                "SUPPORT-0001",
                ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation,
                at,
                ProviderOrderRef: attempt.ProviderOrderRef,
                QuiescenceRef: "QUIESCENCE-0001",
                StoppedAt: at.AddSeconds(-1),
                StoppedInstanceIds: new EquatableArray<Guid>(new[] { sender }),
                SupplierFinalNoEffectsConfirmed: true,
                WalletFinalNoEffectsConfirmed: true,
                SenderEgressIsolated: true,
                OldInstancesCannotResume: true
            )
        );
        booking
            .DecideManualResolution(Guid.NewGuid(), input, at)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        booking.HasConfirmationBarrier.ShouldBeTrue();
        booking.CurrentConfirmationAttempt!.CapturedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(0, 310)]
    [InlineData(0, 311)]
    [InlineData(1, 310)]
    [InlineData(1, 311)]
    [InlineData(2, 310)]
    [InlineData(2, 311)]
    public void Expired_admission_cannot_claim_effects_when_watchdog_is_delayed(
        int stage,
        int seconds
    )
    {
        var booking =
            stage == 2 ? ConfirmationBarrierTests.Started()
            : stage == 1 ? CancellationDecisionTests.Prepared()
            : CancellationDecisionTests.Held();
        CancellationDecision decision;
        if (stage == 2)
        {
            var attempt = booking.CurrentConfirmationAttempt!;
            decision = booking.DecideConfirmationEffectsClaim(
                attempt.Id,
                attempt.Revision,
                attempt.AdmissionId,
                Guid.NewGuid(),
                Now.AddSeconds(seconds)
            );
        }
        else
        {
            var data = CancellationTestData.Input();
            if (stage == 0)
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
            else
                CancellationDecisionTests.Apply(
                    booking,
                    CancellationDecisionTests.Consent(booking)
                );
            var op = booking.CurrentCancellation!;
            decision =
                stage == 0
                    ? booking.DecideCancellationPreparationDispatch(
                        op.Id,
                        op.Revision,
                        op.PreparationAdmissionId,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Now.AddSeconds(seconds)
                    )
                    : booking.DecideCancellationConfirmationDispatch(
                        op.Id,
                        op.Revision,
                        op.ConfirmationAdmissionId!.Value,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Now.AddSeconds(seconds)
                    );
        }
        decision.Events.OfType<CancellationPreparationDispatched>().ShouldBeEmpty();
        decision.Events.OfType<CancellationConfirmationDispatched>().ShouldBeEmpty();
        decision.Events.OfType<ConfirmationEffectsClaimed>().ShouldBeEmpty();
        if (decision.Kind == CancellationDecisionKind.Allowed)
            CancellationDecisionTests.Apply(booking, decision);
        booking.Status.ShouldBe(Travel.Modules.Flights.Core.Aggregates.BookingStatus.Held);
    }
}
