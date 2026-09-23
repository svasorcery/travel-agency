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
                row.BookedAt = held.HeldAt;
                break;
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
            row.BookedAt == default
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
