using System.Globalization;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record AncillaryCatalogRequest(
    Guid AggregateId,
    Guid QuoteRevision,
    bool IncludeSeats = false
);

public sealed record MoneyStringDto(string Amount, string Currency)
{
    public static MoneyStringDto From(Money value) =>
        new(value.Amount.ToString(CultureInfo.InvariantCulture), value.Currency.Value);
}

public sealed record PurchaseServiceDto(
    string SelectionKey,
    string Kind,
    Guid BookingPassengerId,
    BookingSegmentAddress[] Segments,
    int Quantity,
    MoneyStringDto LineTotal,
    string? SeatDesignator,
    string[]? Disclosures,
    BaggageLimits? Baggage
)
{
    public static PurchaseServiceDto From(BookingService service) =>
        new(
            service.Reference,
            service.Kind == BookingServiceKind.Seat ? "seat" : "checked-baggage",
            service.PassengerId,
            service.Segments.ToArray(),
            service.Quantity,
            MoneyStringDto.From(service.LineTotal),
            service.SeatDesignator,
            service.Disclosures?.ToArray(),
            service.Baggage
        );
}

public sealed record BookingPurchaseDto(
    Guid QuoteRevision,
    MoneyStringDto BaseFare,
    MoneyStringDto Extras,
    MoneyStringDto Total,
    PurchaseServiceDto[] Services,
    DateTimeOffset ExpiresAt,
    string NoticeVersion
)
{
    public static BookingPurchaseDto From(BookingPurchase purchase) =>
        new(
            purchase.QuoteRevision,
            MoneyStringDto.From(purchase.BaseFare),
            MoneyStringDto.From(purchase.Extras),
            MoneyStringDto.From(purchase.Total),
            purchase.Services.Select(PurchaseServiceDto.From).ToArray(),
            purchase.ExpiresAt,
            purchase.NoticeVersion
        );
}

public sealed record AncillaryServiceDto(
    string SelectionKey,
    string? Kind,
    Guid[] BookingPassengerIds,
    BookingSegmentAddress[] Segments,
    MoneyStringDto? UnitPrice,
    int MaximumQuantity,
    bool Selectable,
    string? Reason,
    string? Name,
    string? SeatDesignator,
    string? PhysicalSeat,
    string[]? Disclosures,
    BaggageLimits? Baggage
);

public sealed record IncludedBaggageDto(
    Guid BookingPassengerId,
    BookingSegmentAddress Segment,
    int? CheckedQuantity,
    int? CarryOnQuantity
);

public sealed record AncillaryCatalogResponse(
    Guid AggregateId,
    Guid QuoteRevision,
    MoneyStringDto BaseFare,
    DateTimeOffset ExpiresAt,
    AncillaryServiceDto[] Services,
    IncludedBaggageDto[] Allowances,
    AncillarySeatMap[] SeatMaps,
    bool SeatsUnavailable,
    bool UnsupportedPricing
)
{
    public static AncillaryCatalogResponse From(AncillaryCatalogResult result)
    {
        var ids = result.Binding.Slots.ToDictionary(
            s => s.SupplierReference.Value,
            s => s.Id.Value
        );
        var facts = result.Catalog;
        return new(
            result.AggregateId,
            result.Binding.Revision,
            MoneyStringDto.From(facts.Offer.TotalAmount),
            facts.Offer.ExpiresAt,
            facts
                .Services.Select(s => new AncillaryServiceDto(
                    s.Key,
                    s.Kind switch
                    {
                        BookingServiceKind.Seat => "seat",
                        BookingServiceKind.CheckedBaggage => "checked-baggage",
                        _ => null,
                    },
                    s.PassengerReferences.Select(p => ids[p]).ToArray(),
                    s.Segments.ToArray(),
                    s.UnitPrice is { } price ? MoneyStringDto.From(price) : null,
                    s.MaximumQuantity,
                    s.Selectable,
                    s.Reason,
                    s.Name,
                    s.SeatDesignator,
                    s.PhysicalSeat,
                    s.Disclosures?.ToArray(),
                    s.Baggage
                ))
                .ToArray(),
            facts
                .Allowances.Select(a => new IncludedBaggageDto(
                    ids[a.PassengerReference],
                    a.Segment,
                    a.CheckedQuantity,
                    a.CarryOnQuantity
                ))
                .ToArray(),
            facts.SeatMaps.ToArray(),
            facts.SeatsUnavailable,
            facts.UnsupportedPricing
        );
    }
}
