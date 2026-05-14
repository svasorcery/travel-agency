using System.Text.Json;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Api.Contracts;

// ── Search ────────────────────────────────────────────────────────────────────

public sealed record SearchRequest(
    string Origin,
    string Destination,
    DateOnly DepartureDate,
    DateOnly? ReturnDate,
    int PassengerCount = 1,
    string CabinClass = "economy",
    string Currency = "RUB"
);

public sealed record OfferDto(
    Guid Id,
    string Provider,
    decimal TotalAmount,
    string Currency,
    ItineraryDto Itinerary,
    DateTimeOffset FetchedAt,
    DateTimeOffset? ExpiresAt,
    string? ProviderOfferRef,
    string? DeeplinkUrl,
    string? PartnerName
)
{
    public static OfferDto From(Offer offer) =>
        offer switch
        {
            BookableOffer b => new OfferDto(
                Id: b.Id.Value,
                Provider: b.Provider.Value,
                TotalAmount: b.TotalAmount.Amount,
                Currency: b.TotalAmount.Currency.Value,
                Itinerary: ItineraryDto.From(b.Itinerary),
                FetchedAt: b.FetchedAt,
                ExpiresAt: b.ExpiresAt,
                ProviderOfferRef: b.ProviderOfferRef,
                DeeplinkUrl: null,
                PartnerName: null
            ),
            DeeplinkOffer d => new OfferDto(
                Id: d.Id.Value,
                Provider: d.Provider.Value,
                TotalAmount: d.TotalAmount.Amount,
                Currency: d.TotalAmount.Currency.Value,
                Itinerary: ItineraryDto.From(d.Itinerary),
                FetchedAt: d.FetchedAt,
                ExpiresAt: null,
                ProviderOfferRef: null,
                DeeplinkUrl: d.DeeplinkUrl.ToString(),
                PartnerName: d.PartnerName
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(offer), "Unknown offer type."),
        };
}

public sealed record ItineraryDto(SliceDto[] Slices, TimeSpan TotalDuration, bool IsRoundTrip)
{
    public static ItineraryDto From(Itinerary itinerary) =>
        new(
            Slices: itinerary.Slices.Select(SliceDto.From).ToArray(),
            TotalDuration: itinerary.TotalDuration.Value,
            IsRoundTrip: itinerary.IsRoundTrip
        );
}

public sealed record SliceDto(
    string Origin,
    string Destination,
    SegmentDto[] Segments,
    TimeSpan Duration
)
{
    public static SliceDto From(Slice slice) =>
        new(
            Origin: slice.Origin.Value,
            Destination: slice.Destination.Value,
            Segments: slice.Segments.Select(SegmentDto.From).ToArray(),
            Duration: slice.Duration.Value
        );
}

public sealed record SegmentDto(
    string Origin,
    string Destination,
    DateTimeOffset DepartAt,
    DateTimeOffset ArriveAt,
    string CarrierCode,
    string FlightNumber,
    string CabinClass
)
{
    public static SegmentDto From(Segment seg) =>
        new(
            Origin: seg.Origin.Value,
            Destination: seg.Destination.Value,
            DepartAt: seg.DepartAt,
            ArriveAt: seg.ArriveAt,
            CarrierCode: seg.CarrierCode,
            FlightNumber: seg.FlightNumber,
            CabinClass: seg.Cabin.Code
        );
}

public sealed record PartialFailureDto(string Provider, string ErrorCode, long ElapsedMs);

public sealed record SearchResponse(OfferDto[] Offers, PartialFailureDto[] PartialFailures);

// ── NL Search ─────────────────────────────────────────────────────────────────

public sealed record NlSearchRequest(string Query, string Locale = "ru");

// ── Quote ─────────────────────────────────────────────────────────────────────

public sealed record QuoteOfferRequest(string ProviderOfferRef, string Provider);

public sealed record QuotedOfferResponse(Guid AggregateId, OfferDto Offer);

// ── Hold ──────────────────────────────────────────────────────────────────────

public sealed record HoldOfferRequest(Guid AggregateId, PassengerInfoDto[] Passengers);

public sealed record PassengerInfoDto(
    string GivenName,
    string FamilyName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone
);

public sealed record HeldOrderResponse(
    Guid AggregateId,
    string ProviderOrderId,
    DateTimeOffset HeldUntil
);

// ── Confirm ───────────────────────────────────────────────────────────────────

public sealed record ConfirmOrderRequest(Guid AggregateId);

public sealed record ConfirmedOrderResponse(Guid AggregateId, string Status, string? PaymentRef);

// ── Cancel ────────────────────────────────────────────────────────────────────

public sealed record CancelOrderRequest(Guid AggregateId);

// ── Order views ───────────────────────────────────────────────────────────────

public sealed record OrderResponse(
    Guid AggregateId,
    string Status,
    decimal TotalAmount,
    string Currency,
    ItineraryDto Itinerary,
    string[] TicketNumbers,
    DateTimeOffset BookedAt,
    DateTimeOffset? TicketedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? RefundedAt
);

public sealed record OrderListResponse(OrderResponse[] Items, int Limit, int Offset);

// ── Mapping helpers ────────────────────────────────────────────────────────────

public static class OrderResponseMapper
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public static OrderResponse From(Application.Queries.OrderView view)
    {
        ItineraryDto itinerary;
        try
        {
            // Try deserializing as the domain Itinerary first, then map
            var domainItinerary = JsonSerializer.Deserialize<Itinerary>(
                view.ItineraryJson,
                _jsonOpts
            );
            itinerary = domainItinerary is not null
                ? ItineraryDto.From(domainItinerary)
                : JsonSerializer.Deserialize<ItineraryDto>(view.ItineraryJson, _jsonOpts)
                    ?? new ItineraryDto([], TimeSpan.Zero, false);
        }
        catch
        {
            // Fallback: try deserializing directly as ItineraryDto
            try
            {
                itinerary =
                    JsonSerializer.Deserialize<ItineraryDto>(view.ItineraryJson, _jsonOpts)
                    ?? new ItineraryDto([], TimeSpan.Zero, false);
            }
            catch
            {
                itinerary = new ItineraryDto([], TimeSpan.Zero, false);
            }
        }

        return new OrderResponse(
            AggregateId: view.AggregateId,
            Status: view.Status,
            TotalAmount: view.TotalAmount,
            Currency: view.Currency,
            Itinerary: itinerary,
            TicketNumbers: view.TicketNumbers.ToArray(),
            BookedAt: view.BookedAt,
            TicketedAt: view.TicketedAt,
            CancelledAt: view.CancelledAt,
            RefundedAt: view.RefundedAt
        );
    }
}
