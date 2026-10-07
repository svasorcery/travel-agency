using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Providers.Dtos;

public sealed record AncillaryCatalogFacts(
    BookableOffer Offer,
    EquatableArray<AncillaryService> Services,
    EquatableArray<IncludedBaggageFact> Allowances,
    EquatableArray<AncillarySeatMap> SeatMaps,
    bool SeatsRequested,
    bool SeatsUnavailable = false,
    bool UnsupportedPricing = false
);

public sealed record IncludedBaggageFact(
    string PassengerReference,
    BookingSegmentAddress Segment,
    int? CheckedQuantity,
    int? CarryOnQuantity
);

public sealed record AncillaryService(
    string Key,
    BookingServiceKind? Kind,
    EquatableArray<string> PassengerReferences,
    EquatableArray<BookingSegmentAddress> Segments,
    Money? UnitPrice,
    int MaximumQuantity,
    bool Selectable,
    string? Reason = null,
    string? Name = null,
    string? SeatDesignator = null,
    string? PhysicalSeat = null,
    EquatableArray<string>? Disclosures = null,
    BaggageLimits? Baggage = null
);

public sealed record AncillarySeatMap(
    BookingSegmentAddress Segment,
    EquatableArray<AncillarySeatCabin> Cabins
);

public sealed record AncillarySeatCabin(
    int Deck,
    string Cabin,
    EquatableArray<AncillarySeatRow> Rows
);

public sealed record AncillarySeatRow(EquatableArray<AncillarySeatSection> Sections);

public sealed record AncillarySeatSection(EquatableArray<AncillarySeatElement> Elements);

public sealed record AncillarySeatElement(
    string Kind,
    string? Designator,
    EquatableArray<string> ServiceKeys
);

public sealed record AncillarySelection(string SelectionKey, int Quantity);
