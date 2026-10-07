using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private CancellationDecision DecideCreationResolution(
        Guid actor,
        ManualResolutionInput input,
        string payload,
        ManualResolutionEvidence evidence,
        DateTimeOffset now
    )
    {
        if (
            CurrentCreation is not { } attempt
            || attempt.Id != input.TargetId
            || attempt.Revision != input.ExpectedRevision
            || attempt.CreationCompleted
            || attempt.Outcome == BookingCreationOutcome.NotCreated
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        if (input.Decision == ManualResolutionDecisionKind.RecordInconclusive)
            return AppendManualAudit(
                actor,
                input,
                payload,
                evidence,
                CancellationResolutionSource.OperatorVerified,
                AllowCancellation(attempt.Id),
                now
            );
        var proof = evidence.CreationEvidence;
        if (
            proof is null
            || !proof.SupplierOperationCompleted
            || evidence.ObservedAt < attempt.StartedAt
            || !(
                attempt.SenderCompleted
                || ValidSenderStop(evidence, attempt.SenderInstanceId, attempt.StartedAt, now)
            )
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        BookingCreationObservation observation;
        if (input.Decision == ManualResolutionDecisionKind.ConfirmNoCreatedOrder)
        {
            if (
                proof.Order is not null
                || !proof.NoCreatedOrPendingOrderConfirmed
                || evidence.Category != ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation
                || !evidence.SupplierFinalNoEffectsConfirmed
            )
                return RejectCancellation(CancellationReason.InconsistentEvidence);
            observation = new(
                BookingCreationOutcome.NotCreated,
                null,
                null,
                false,
                true,
                "OperatorNoEffects",
                evidence.ObservedAt,
                BookingEvidenceSource.OperatorVerified,
                true
            );
        }
        else
        {
            if (
                input.Decision
                    is not (
                        ManualResolutionDecisionKind.AttachMatches
                        or ManualResolutionDecisionKind.AttachDifferences
                    )
                || proof.Order is not { } actual
                || proof.NoCreatedOrPendingOrderConfirmed
                || actual.ProviderOrderId != evidence.ProviderOrderRef
                || attempt.KnownOrderId is { } known && known != actual.ProviderOrderId
                || evidence.Category
                    is not (
                        ManualEvidenceCategory.SupplierSupportAttestation
                        or ManualEvidenceCategory.StoredSupplierObservation
                    )
                || HasConfirmationBarrier
                || CurrentCancellation is { IsTerminal: false }
            )
                return RejectCancellation(CancellationReason.InconsistentEvidence);
            observation = new(
                input.Decision == ManualResolutionDecisionKind.AttachMatches
                    ? BookingCreationOutcome.Matches
                    : BookingCreationOutcome.CreatedWithDifferences,
                actual,
                actual.ProviderOrderId,
                true,
                true,
                "OperatorOrderObserved",
                evidence.ObservedAt,
                BookingEvidenceSource.OperatorVerified
            );
        }
        var observed = DecideCreationObservation(attempt.Id, attempt.Revision, observation, now);
        if (observed.IsError || observed.Value.Event is null)
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        List<IDomainEvent> events = [observed.Value.Event];
        if (observation.Order is { } order)
        {
            events.Add(
                new OfferHeldV3(
                    order.ProviderOrderId,
                    attempt.ProtectedParty,
                    order.PaymentRequiredBy,
                    now,
                    attempt.OwnerId,
                    attempt.QuoteRevision,
                    PassengerCount
                )
            );
            events.Add(new BookingMutationCoordinationEnabled(now));
        }
        return AppendManualAudit(
            actor,
            input,
            payload,
            evidence,
            CancellationResolutionSource.OperatorVerified,
            AllowCancellation(attempt.Id, events.ToArray()),
            now
        );
    }
}
