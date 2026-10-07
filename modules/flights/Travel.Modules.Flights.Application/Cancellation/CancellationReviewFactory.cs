using System.Globalization;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Application.Cancellation;

public static class CancellationReviewFactory
{
    public static CancellationReviewResult Create(
        BookingAggregate booking,
        long version,
        DateTimeOffset now
    )
    {
        CancellationReviewTarget? target = null;
        if (booking.CurrentConfirmationAttempt is { IsTerminal: false } attempt)
            target = new(
                "Confirmation",
                attempt.Id,
                attempt.Revision,
                attempt.Phase.ToString(),
                attempt.ProviderOrderRef,
                null,
                attempt.DispatchOwnerInstanceId,
                attempt.EffectsClaimedAt,
                attempt.PaymentReference?.ToString(),
                attempt.SupplierPaymentEvidence?.Reference,
                null,
                attempt.AcceptedMoney?.Amount.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture
                ),
                attempt.AcceptedMoney?.Currency.Value
            );
        else if (booking.Status == BookingStatus.Held && !booking.MutationCoordinationEnabled)
            target = new(
                "LegacyHeld",
                booking.Id,
                version,
                "Unverified",
                booking.ProviderOrderId!,
                null,
                null,
                null,
                null,
                null
            );
        else if (booking.CurrentCreation is { IsUnresolved: true } creation)
            target = new(
                "Creation",
                creation.Id,
                creation.Revision,
                creation.Outcome.ToString(),
                creation.KnownOrderId ?? "",
                null,
                creation.SenderInstanceId,
                creation.StartedAt,
                null,
                null
            );
        else if (booking.CurrentCancellation is { } op)
            target = new(
                "Cancellation",
                op.Id,
                op.Revision,
                op.Phase.ToString(),
                op.ProviderOrderRef,
                op.ProviderCancellationRef,
                op.DispatchOwnerInstanceId,
                op.ConfirmationDispatchedAt ?? op.PreparationDispatchedAt,
                null,
                null,
                op.ItineraryPartyHash
            );
        return new(
            CancellationStatusFactory.Create(booking, version, null, now),
            target,
            booking
                .ManualReviewHistory.Select(e => new CancellationReviewAudit(
                    e.ResolutionId,
                    e.ActorId,
                    e.Decision.ToString(),
                    e.Source.ToString(),
                    e.Evidence.EvidenceRef,
                    e.OccurredAt
                ))
                .ToArray(),
            booking.CurrentCreation is { } current
            && booking.QuoteBinding is { } binding
            && booking.Itinerary is { } itinerary
                ? new(
                    current.OwnerId,
                    booking.ProviderOfferRef!,
                    current.QuoteRevision,
                    new(binding.Slots.Select(s => s.Id.Value).ToArray()),
                    itinerary,
                    current.Accepted
                )
                : null
        );
    }
}
