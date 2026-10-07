using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using FlightJourneyKind = Travel.Modules.Flights.Core.ValueObjects.JourneyKind;

namespace Travel.Modules.Flights.Api.Contracts;

// ── Search ────────────────────────────────────────────────────────────────────

/// <summary>
/// Search request body. <c>Currency</c> is intentionally absent — it is bound from the
/// <c>?currency=</c> query parameter by <see cref="Endpoints.SearchEndpoint"/>.
/// <c>Locale</c> is read from the <c>Accept-Language</c> header.
/// </summary>
public sealed record SearchRequest(
    string Origin,
    string Destination,
    DateOnly DepartureDate,
    DateOnly? ReturnDate,
    int PassengerCount = 1,
    string CabinClass = "economy"
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
    string? PartnerName,
    int? PassengerCount = null,
    bool? HoldEligible = null,
    string? HoldIneligibilityReason = null
)
{
    private static string? EligibilityReason(BookableOfferParty? party) =>
        party switch
        {
            { SupportsHold: true, RequiresIdentityDocuments: false } => null,
            { SupportsHold: false } => "hold-not-supported",
            { RequiresIdentityDocuments: true } => "identity-documents-required",
            _ => "capability-unknown",
        };

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
                PartnerName: null,
                PassengerCount: b.Party?.PassengerCount,
                HoldEligible: b.Party is { SupportsHold: true, RequiresIdentityDocuments: false },
                HoldIneligibilityReason: EligibilityReason(b.Party)
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

public sealed record ItineraryDto(
    SliceDto[] Slices,
    TimeSpan TotalDuration,
    bool IsRoundTrip,
    string? JourneyKind = null
)
{
    public static ItineraryDto From(Itinerary itinerary)
    {
        var slices = (itinerary.Slices ?? Array.Empty<Slice>()).Select(SliceDto.From).ToArray();
        var kind = itinerary.Slices is { Count: > 0 }
            ? itinerary.JourneyKind switch
            {
                FlightJourneyKind.OneWay => "one-way",
                FlightJourneyKind.RoundTrip => "round-trip",
                _ => "multi-leg",
            }
            : null;
        return new(
            Slices: slices,
            TotalDuration: itinerary.TotalDuration?.Value ?? TimeSpan.Zero,
            IsRoundTrip: kind == "round-trip",
            JourneyKind: kind
        );
    }

    // Historical flat DTOs retain their stored timestamps and durations; kind is geometry, not a version.
    internal ItineraryDto NormalizeGeometry()
    {
        // Keep malformed history intact for the client's strict contract-error path.
        if (Slices is null || Slices.Any(slice => slice is null))
            return this;
        var slices = Slices ?? [];
        var kind = slices.Length switch
        {
            0 => null,
            1 => "one-way",
            2
                when slices[1].Origin == slices[0].Destination
                    && slices[1].Destination == slices[0].Origin => "round-trip",
            _ => "multi-leg",
        };
        return this with
        {
            Slices = slices,
            IsRoundTrip = kind == "round-trip",
            JourneyKind = kind,
        };
    }
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

public sealed record SkippedProviderDto(string Provider, string ReasonCode);

public sealed record PartialFailureDto(string Provider, string ErrorCode, long ElapsedMs);

public sealed record SearchResponse(
    OfferDto[] Offers,
    PartialFailureDto[] PartialFailures,
    SkippedProviderDto[] SkippedProviders,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        SearchRankingDto? Ranking = null
)
{
    public static SearchResponse From(SearchResult result) =>
        new(
            result.Offers.Select(OfferDto.From).ToArray(),
            result
                .PartialFailures.Select(f => new PartialFailureDto(
                    f.Provider,
                    f.ErrorCode,
                    f.ElapsedMs
                ))
                .ToArray(),
            (result.SkippedProviders ?? [])
                .Select(p => new SkippedProviderDto(p.Provider, p.ReasonCode))
                .ToArray(),
            result.Ranking is { } ranking ? SearchRankingDto.From(ranking) : null
        );
}

public sealed record SearchRankingDto(
    string Policy,
    string RequestedCurrency,
    OfferRankingEntryDto[] Entries
)
{
    public static SearchRankingDto From(SearchRanking ranking) =>
        new(
            ranking.Policy,
            ranking.RequestedCurrency,
            ranking
                .Entries.Select(e => new OfferRankingEntryDto(
                    e.OfferId,
                    e.Currency,
                    e.Rank,
                    e.SourcePrice.Amount,
                    e.SourcePrice.Currency.Value,
                    e.PriceState switch
                    {
                        RankingPriceState.Native => "native",
                        RankingPriceState.Converted => "converted",
                        RankingPriceState.FxUnavailable => "fx-unavailable",
                        _ => throw new ArgumentOutOfRangeException(nameof(ranking)),
                    },
                    e.DurationSeconds,
                    e.Transfers,
                    e.Limitations.ToArray()
                ))
                .ToArray()
        );
}

public sealed record OfferRankingEntryDto(
    Guid OfferId,
    string Currency,
    int Rank,
    decimal SourceAmount,
    string SourceCurrency,
    string PriceState,
    long? DurationSeconds,
    int? Transfers,
    string[] Limitations
);

// ── NL Search ─────────────────────────────────────────────────────────────────

/// <summary>
/// NL search request body. <c>Locale</c> is intentionally absent — it is read from the
/// <c>Accept-Language</c> header by <see cref="Endpoints.NlSearchEndpoint"/>.
/// </summary>
public sealed record NlSearchRequest(string Query);

// ── Quote ─────────────────────────────────────────────────────────────────────

public sealed record QuoteOfferRequest(
    string ProviderOfferRef,
    string Provider,
    Guid? AggregateId = null,
    int PassengerCount = 1,
    AncillarySelection[]? Selections = null
);

/// <summary>
/// Response to a quote/re-quote request.
/// When <see cref="PriceChanged"/> is <c>true</c>, the provider returned a different price
/// than the previously cached offer: <see cref="OldAmount"/>/<see cref="OldCurrency"/> hold the
/// cached price and <see cref="NewAmount"/>/<see cref="NewCurrency"/> hold the live price.
/// </summary>
public sealed record QuotedOfferResponse(
    Guid AggregateId,
    OfferDto Offer,
    FareConditionsDto FareConditions,
    QuoteBindingDto Binding,
    bool PriceChanged = false,
    decimal? OldAmount = null,
    string? OldCurrency = null,
    decimal? NewAmount = null,
    string? NewCurrency = null,
    BookingPurchaseDto? Purchase = null
);

/// <summary>
/// Fare facts from the refreshed bookable offer. Baggage quantities currently reflect the
/// provider mapper's maximum across segments; they are not a per-segment allowance.
/// </summary>
public sealed record FareConditionsDto(
    bool ChangeAllowed,
    bool RefundAllowed,
    string? FareBasisCode,
    string? CabinClassMarketing,
    int CheckedBaggageQuantity,
    int CarryOnBaggageQuantity
)
{
    public static FareConditionsDto From(FareConditions conditions) =>
        new(
            conditions.ChangeAllowed,
            conditions.RefundAllowed,
            conditions.FareBasisCode,
            conditions.CabinClassMarketing,
            conditions.CheckedBaggageQuantity,
            conditions.CarryOnBaggageQuantity
        );
}

// ── Hold ──────────────────────────────────────────────────────────────────────

[method: JsonConstructor]
public sealed record HoldOfferRequest(
    Guid AggregateId,
    PassengerInfoDto[] Passengers,
    Guid QuoteRevision,
    bool AcceptAncillaries = false
)
{
    // Keep missing-binding callers on the endpoint's typed validation path. Optional Guid
    // constructor defaults are reflected as null and cannot be exported as a Guid schema.
    public HoldOfferRequest(Guid AggregateId, PassengerInfoDto[] Passengers)
        : this(AggregateId, Passengers, Guid.Empty) { }
}

[method: JsonConstructor]
public sealed record PassengerInfoDto(
    string GivenName,
    string FamilyName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone,
    Guid BookingPassengerId,
    string Title = ""
)
{
    public PassengerInfoDto(
        string GivenName,
        string FamilyName,
        DateOnly DateOfBirth,
        string Gender,
        string Email,
        string Phone
    )
        : this(GivenName, FamilyName, DateOfBirth, Gender, Email, Phone, Guid.Empty) { }
}

public sealed record HeldOrderResponse(
    Guid AggregateId,
    string ProviderOrderId,
    DateTimeOffset HeldUntil
);

// ── Confirm ───────────────────────────────────────────────────────────────────

public sealed record ConfirmOrderRequest(Guid AggregateId);

public sealed record ConfirmedOrderResponse(Guid AggregateId, string Status, string? PaymentRef);

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
    DateTimeOffset? RefundedAt,
    int PassengerCount
);

public sealed record OrderListResponse(OrderResponse[] Items, int Limit, int Offset);

// ── Mapping helpers ────────────────────────────────────────────────────────────

public static class OrderResponseMapper
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Maps an <see cref="Application.Queries.OrderView"/> to an <see cref="OrderResponse"/>.
    /// If <paramref name="logger"/> is supplied, a warning is emitted on malformed
    /// <c>ItineraryJson</c> instead of silently swallowing the exception.
    /// </summary>
    public static OrderResponse From(Application.Queries.OrderView view, ILogger? logger = null)
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
        catch (JsonException ex)
        {
            logger?.LogWarning(
                ex,
                "ItineraryJson for order {AggregateId} could not be deserialized as Itinerary; attempting ItineraryDto fallback.",
                view.AggregateId
            );
            // Fallback: try deserializing directly as ItineraryDto
            try
            {
                itinerary =
                    JsonSerializer.Deserialize<ItineraryDto>(view.ItineraryJson, _jsonOpts)
                    ?? new ItineraryDto([], TimeSpan.Zero, false);
            }
            catch (JsonException ex2)
            {
                logger?.LogWarning(
                    ex2,
                    "ItineraryJson for order {AggregateId} is malformed; returning empty itinerary.",
                    view.AggregateId
                );
                itinerary = new ItineraryDto([], TimeSpan.Zero, false);
            }
        }

        return new OrderResponse(
            AggregateId: view.AggregateId,
            Status: view.Status,
            TotalAmount: view.TotalAmount,
            Currency: view.Currency,
            Itinerary: itinerary.NormalizeGeometry(),
            TicketNumbers: view.TicketNumbers.ToArray(),
            BookedAt: view.BookedAt,
            TicketedAt: view.TicketedAt,
            CancelledAt: view.CancelledAt,
            RefundedAt: view.RefundedAt,
            PassengerCount: view.PassengerCount
        );
    }
}

public sealed record QuotePassengerSlotDto(Guid BookingPassengerId, string Kind);

public sealed record QuoteBindingDto(
    Guid Revision,
    int PassengerCount,
    DateOnly FirstDepartureLocalDate,
    QuotePassengerSlotDto[] Slots
)
{
    public static QuoteBindingDto From(QuoteBinding binding) =>
        new(
            binding.Revision,
            binding.Party.PassengerCount,
            binding.Party.FirstDepartureLocalDate,
            binding.Slots.Select(s => new QuotePassengerSlotDto(s.Id.Value, "adult")).ToArray()
        );
}
