using System.Globalization;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Application.Cancellation;

public static class CancellationStatusFactory
{
    public static CancellationStatusResult Create(
        BookingAggregate booking,
        long bookingVersion,
        Guid? requested,
        DateTimeOffset now
    )
    {
        var selected = requested ?? booking.CurrentCancellationId;
        var operation =
            selected is { } id && booking.CancellationOperations.TryGetValue(id, out var found)
                ? found
                : null;
        CancellationOperationStatus? view = null;
        if (operation is { } op)
        {
            CancellationTermsView? terms = op.Terms is { } quote
                ? new(
                    quote.Revision,
                    quote.Hash,
                    quote.Refund.Amount.ToString(
                        "0.############################",
                        CultureInfo.InvariantCulture
                    ),
                    quote.Refund.Currency.Value,
                    quote.Destination.ToString(),
                    quote.ExpiresAt,
                    quote.NoticeVersion,
                    booking.PassengerCount,
                    booking.Itinerary,
                    quote.Settlement.Provenance.ToString()
                )
                : null;
            view = new(
                op.Id,
                op.Revision,
                op.Phase.ToString(),
                op.UnknownStage.ToString(),
                op.Outcome.ToString(),
                op.ResolutionSource.ToString(),
                op.Reason.ToString(),
                op.ConfirmationDispatchedAt is not null ? "ConfirmationClaimed"
                    : op.PreparationDispatchedAt is not null ? "PreparationClaimed"
                    : "NotDispatched",
                terms,
                booking.NextOwnerRefreshAt,
                op.Outcome == CancellationOutcome.Succeeded ? bookingVersion : null,
                !op.IsTerminal
                    && (
                        (op.ActiveReadId is not null && op.Recovery?.ReadWindowUntil > now)
                        || (
                            op.LastRefreshRequestId is { } refresh
                            && refresh != op.ConsumedRefreshRequestId
                        )
                    )
            );
        }
        BlockingConfirmationStatus? blocker = null;
        if (booking.CurrentConfirmationAttempt is { IsTerminal: false } attempt)
            blocker = new(
                "ConfirmationAttempt",
                attempt.Id,
                attempt.Revision,
                attempt.Phase.ToString(),
                attempt.Reason.ToString(),
                attempt.EffectsClaimedAt is null
            );
        else if (booking.Status == BookingStatus.Held && !booking.MutationCoordinationEnabled)
            blocker = new(
                "LegacyHeld",
                booking.Id,
                bookingVersion,
                "ManualReviewRequired",
                "LegacyConfirmationUnverified",
                false
            );
        return new(
            booking.Id,
            booking.Status.ToString(),
            bookingVersion,
            requested,
            booking.CurrentCancellationId,
            selected == booking.CurrentCancellationId,
            view,
            blocker,
            now,
            booking.ObservedSupplierCancellation is { } fact
                ? new SupplierOrderCancellationStatus(
                    fact.CancelledAt,
                    fact.Source.ToString(),
                    operation?.Outcome == CancellationOutcome.Succeeded
                )
                : null
        );
    }
}
