using System.Text.Json;
using JasperFx.Events;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

/// <summary>Pure event-to-row mapping shared by catch-up, validation and exclusive rebuild.</summary>
public static class OrderReadModelEventApplier
{
    public static void Apply(OrderReadModelEntity row, IEvent envelope)
    {
        if (
            envelope.StreamId != row.AggregateId
            || envelope.Version != row.ProjectedStreamVersion + 1
        )
            throw new BookingProjectionTerminalException("SourceVersionGap");
        if (envelope.Version == 1 && envelope.Data is not OfferQuoted)
            throw new BookingProjectionTerminalException("SourcePayloadInvalid");

        switch (envelope.Data)
        {
            case OfferQuoted quoted:
                row.Status = "OfferQuoted";
                SetPrice(row, quoted.TotalAmount);
                row.ItineraryJson = JsonSerializer.Serialize(quoted.Itinerary);
                break;
            case OfferReQuoted requoted:
                if (requoted.RefreshedOffer is { } refreshed)
                {
                    if (
                        refreshed.Id != requoted.OfferId
                        || refreshed.TotalAmount != requoted.NewAmount
                    )
                        throw new BookingProjectionTerminalException("SourcePayloadInvalid");
                    row.ItineraryJson = JsonSerializer.Serialize(refreshed.Itinerary);
                }
                SetPrice(row, requoted.NewAmount);
                break;
            case OfferHeld held:
                if (held.OwnerUserId is not { } owner || owner == Guid.Empty)
                    throw new BookingProjectionTerminalException("SourceOwnerMissing");
                if (row.UserId is { } priorOwner && priorOwner != owner)
                    throw new BookingProjectionTerminalException("SourceOwnerConflict");
                row.UserId = owner;
                row.Status = "Held";
                row.ProviderOrderId = held.OrderId;
                row.PassengerInfoJson = JsonSerializer.Serialize(held.Passenger);
                row.PassengerCount = 1;
                row.BookedAt = held.HeldAt;
                break;
            case OfferHeldV2 heldV2:
                if (heldV2.OwnerUserId == Guid.Empty)
                    throw new BookingProjectionTerminalException("SourceOwnerMissing");
                if (row.UserId is { } priorOwnerV2 && priorOwnerV2 != heldV2.OwnerUserId)
                    throw new BookingProjectionTerminalException("SourceOwnerConflict");
                if (
                    heldV2.PassengerSnapshot is null
                    || heldV2.PassengerSnapshot.FormatVersion != 1
                    || string.IsNullOrWhiteSpace(heldV2.PassengerSnapshot.Ciphertext)
                )
                    throw new BookingProjectionTerminalException("SourcePayloadInvalid");
                row.UserId = heldV2.OwnerUserId;
                row.Status = "Held";
                row.ProviderOrderId = heldV2.OrderId;
                row.PassengerInfoJson = JsonSerializer.Serialize(heldV2.PassengerSnapshot);
                row.PassengerCount = 1;
                row.BookedAt = heldV2.HeldAt;
                break;
            case OfferHeldV3 heldV3:
                if (heldV3.OwnerUserId == Guid.Empty)
                    throw new BookingProjectionTerminalException("SourceOwnerMissing");
                if (row.UserId is { } priorOwnerV3 && priorOwnerV3 != heldV3.OwnerUserId)
                    throw new BookingProjectionTerminalException("SourceOwnerConflict");
                if (
                    heldV3.PassengerCount is < 1 or > 9
                    || heldV3.QuoteRevision == Guid.Empty
                    || heldV3.PassengerSnapshot is null
                    || heldV3.PassengerSnapshot.FormatVersion != 1
                    || string.IsNullOrWhiteSpace(heldV3.PassengerSnapshot.Ciphertext)
                )
                    throw new BookingProjectionTerminalException("SourcePayloadInvalid");
                row.UserId = heldV3.OwnerUserId;
                row.Status = "Held";
                row.ProviderOrderId = heldV3.OrderId;
                row.PassengerInfoJson = JsonSerializer.Serialize(heldV3.PassengerSnapshot);
                row.PassengerCount = heldV3.PassengerCount;
                row.BookedAt = heldV3.HeldAt;
                break;
            case CancellationObservationRecorded observed
                when observed.Observation.OrderCancellation is { } fact:
                if (row.UserId is null || row.UserId == Guid.Empty)
                    throw new BookingProjectionTerminalException("SourceOwnerMissing");
                if (fact.ProviderOrderRef != row.ProviderOrderId)
                    throw new BookingProjectionTerminalException("SourcePayloadInvalid");
                if (row.Status != "Refunded")
                {
                    row.Status = "Cancelled";
                    row.CancelledAt = fact.CancelledAt;
                }
                break;
            case CancellationManualReviewRequired review when review.OrderCancellation is { } fact:
                if (row.UserId is null || row.UserId == Guid.Empty)
                    throw new BookingProjectionTerminalException("SourceOwnerMissing");
                if (fact.ProviderOrderRef != row.ProviderOrderId)
                    throw new BookingProjectionTerminalException("SourcePayloadInvalid");
                if (row.Status != "Refunded")
                {
                    row.Status = "Cancelled";
                    row.CancelledAt = fact.CancelledAt;
                }
                break;
            case BookingMutationCoordinationEnabled:
            case CancellationPreparationStarted:
            case CancellationPreparationDispatched:
            case CancellationTermsObtained:
            case CancellationTermsWereUnavailable:
            case CancellationTermsAccepted:
            case CancellationConfirmationDispatched:
            case CancellationObservationStarted:
            case CancellationObservationRecorded:
            case CancellationOutcomeBecameUnknown:
            case CancellationSucceeded:
            case CancellationRejected:
            case CancellationManualReviewRequired:
            case BookingOperationReviewRecorded:
            case CancellationReviewWasAbandoned:
            case CancellationReviewExpired:
            case CancellationRefreshRequested:
            case ConfirmationAttemptStarted:
            case ConfirmationEffectsClaimed:
            case ConfirmationPaymentReferenceRecorded:
            case ConfirmationCaptureObserved:
            case ConfirmationAttemptCompleted:
            case ConfirmationAttemptClosedWithoutEffects:
            case ConfirmationAttemptRequiredManualReview:
            case PaymentAuthorized:
                if (row.UserId is null || row.UserId == Guid.Empty)
                    throw new BookingProjectionTerminalException("SourceOwnerMissing");
                break; // No displayed field changes, but the checkpoint must advance.
            case OrderConfirmed confirmed:
                row.Status = "Confirmed";
                row.ProviderOrderId = confirmed.OrderId;
                break;
            case OrderTicketed ticketed:
                row.Status = "Ticketed";
                row.TicketNumbers = ticketed.TicketNumbers.ToArray();
                row.TicketedAt = ticketed.TicketedAt;
                break;
            case OrderCancelled cancelled:
                row.Status = "Cancelled";
                row.CancelledAt = cancelled.CancelledAt;
                break;
            case OrderRefunded refunded:
                row.Status = "Refunded";
                row.RefundedAt = refunded.RefundedAt;
                break;
            default:
                throw new BookingProjectionTerminalException("SourceEventUnsupported");
        }
        row.ProjectedStreamVersion = envelope.Version;
    }

    public static bool ShouldMaterialize(OrderReadModelEntity row)
    {
        if (row.Status == "OfferQuoted" && row.BookedAt == default && row.UserId is null)
            return false;
        if (row.UserId is null || row.UserId == Guid.Empty)
            throw new BookingProjectionTerminalException("SourceOwnerMissing");
        if (
            row.PassengerCount is < 1 or > 9
            || row.BookedAt == default
            || string.IsNullOrEmpty(row.ProviderOrderId)
            || string.IsNullOrEmpty(row.ItineraryJson)
            || string.IsNullOrEmpty(row.PassengerInfoJson)
        )
            throw new BookingProjectionTerminalException("SourcePayloadInvalid");
        return true;
    }

    private static void SetPrice(OrderReadModelEntity row, Money price)
    {
        row.TotalAmount = price.Amount;
        row.Currency = price.Currency.Value;
    }
}
