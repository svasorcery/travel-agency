using ErrorOr;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Core.Refunds;

public static class CustomerRefundPolicy
{
    public static ErrorOr<CustomerRefundAssessment> Assess(
        BookingAggregate booking,
        Guid cancellationId,
        DateTimeOffset now
    )
    {
        if (booking is null || !booking.HasConsistentMutationOwner || booking.Id == Guid.Empty)
            return Error.NotFound("Flights.BookingNotFound", "Booking not found.");
        if (!booking.CancellationOperations.TryGetValue(cancellationId, out var op))
            return new CustomerRefundAssessment(
                booking.Status is BookingStatus.Cancelled or BookingStatus.Refunded
                    ? CustomerRefundState.LegacyUnverified
                    : CustomerRefundState.NotApplicable,
                null,
                null,
                "CancellationNotEstablished"
            );
        var terms = op.Terms;
        if (
            booking.HasConfirmationBarrier
            || booking.HasUnresolvedCreation
            || booking.CurrentCancellation is { IsTerminal: false }
        )
            return Manual("CompetingEffects");
        if (
            op.Outcome != CancellationOutcome.Succeeded
            || terms is null
            || op.Evidence is not { } evidence
            || op.AcceptedAt is null
            || op.OwnerId != booking.OwnerUserId
            || terms.OwnerId != booking.OwnerUserId
            || terms.AggregateId != booking.Id
            || terms.OperationId != op.Id
            || op.ProviderOrderRef != booking.ProviderOrderId
            || terms.ProviderOrderRef != booking.ProviderOrderId
        )
            return Manual("CancellationNotEstablished");
        var facts = new CancellationConfirmationFacts(
            evidence.ProviderOrderRef,
            evidence.ProviderCancellationRef,
            evidence.ItineraryPartyHash,
            evidence.Refund,
            evidence.Destination,
            evidence.Settlement,
            evidence.ConfirmedAt,
            evidence.ObservedAt,
            evidence.Source
        );
        var checkedEvidence =
            evidence.Source == CancellationResolutionSource.SupplierApi
                ? CancellationEvidence.FromSupplierConfirmation(terms, facts, now)
                : CancellationEvidence.FromOperatorConfirmation(terms, facts, now);
        if (checkedEvidence.IsError || terms.Refund.Amount < 0m)
            return Manual("CancellationEvidenceMismatch");

        if (terms.Settlement.Composition == SettlementComposition.Unpaid)
        {
            var creation = booking.CurrentCreation;
            var noEffects = booking.ConfirmationAttempts.Values.All(a =>
                a.IsTerminal && a.EffectsClaimedAt is null && a.CapturedAt is null
            );
            return
                booking.MutationCoordinationEnabled
                && booking.PaymentRef is null
                && noEffects
                && creation
                    is {
                        SenderCompleted: true,
                        CreationCompleted: true,
                        Outcome: BookingCreationOutcome.Matches
                            or BookingCreationOutcome.CreatedWithDifferences,
                        Actual.AwaitingPayment: true
                    }
                && creation.Actual.ProviderOrderId == booking.ProviderOrderId
                && terms.Refund.Amount == 0m
                && terms.Settlement.UnpaidOrderVerified
                && terms.Destination == CancellationRefundDestination.AwaitingPayment
                ? new CustomerRefundAssessment(
                    CustomerRefundState.NoCustomerPayment,
                    terms.Refund,
                    null,
                    "ProvenUnpaid"
                )
                : Manual("PaymentNotEstablished");
        }

        var confirmation = booking.CurrentConfirmationAttempt;
        if (
            confirmation
                is not {
                    Phase: ConfirmationAttemptPhase.Completed,
                    CapturedAt: not null,
                    PaymentReference: not null,
                    AcceptedMoney: not null,
                    SupplierPaymentEvidence: not null
                }
            || confirmation.ProviderOrderRef != booking.ProviderOrderId
            || confirmation.PaymentReference != booking.PaymentRef
            || confirmation.AcceptedMoney != confirmation.SupplierPaymentEvidence.Amount
            || confirmation.SupplierPaymentEvidence.Kind != SupplierPaymentKind.Balance
            || confirmation.CapturedAt > confirmation.CompletedAt
            || confirmation.CompletedAt > evidence.ConfirmedAt
            || confirmation.CapturedAt > now
        )
            return new CustomerRefundAssessment(
                CustomerRefundState.LegacyUnverified,
                null,
                null,
                "CaptureNotEstablished"
            );
        var basis = CustomerPaymentBasis.Create(
            booking.Id,
            booking.OwnerUserId!.Value,
            confirmation.Id,
            confirmation.PaymentReference.Value,
            confirmation.AcceptedMoney,
            confirmation.CapturedAt.Value
        );
        if (basis.IsError)
            return new CustomerRefundAssessment(
                CustomerRefundState.LegacyUnverified,
                null,
                null,
                "CaptureNotEstablished"
            );
        if (terms.Refund.Currency != basis.Value.CapturedAmount.Currency)
            return Manual("CurrencyMismatch");
        if (terms.Refund.Amount > basis.Value.CapturedAmount.Amount)
            return Manual("AmountExceedsCapture");
        if (
            !terms.Settlement.CreditsKnownEmpty
            || terms.Settlement.Composition != SettlementComposition.CashOnly
            || terms.Destination != CancellationRefundDestination.Balance
        )
            return Manual("SettlementUnsupported");
        if (
            terms.Settlement.Provenance != CancellationResolutionSource.SupplierApi
            || evidence.Source != CancellationResolutionSource.SupplierApi
            || confirmation.ResolutionSource != CancellationResolutionSource.SupplierApi
        )
            return new CustomerRefundAssessment(
                CustomerRefundState.ManualReviewRequired,
                terms.Refund,
                basis.Value,
                "OperatorVerificationRequired"
            );
        return new CustomerRefundAssessment(
            terms.Refund.Amount == 0m ? CustomerRefundState.NoRefundDue : CustomerRefundState.Ready,
            terms.Refund,
            basis.Value,
            "None"
        );
    }

    private static CustomerRefundAssessment Manual(string reason) =>
        new(CustomerRefundState.ManualReviewRequired, null, null, reason);
}
