using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelSeatMapsResponseDto(
    [property: JsonPropertyName("data")] DuffelSeatMapDto[]? Data
);

public sealed record DuffelSeatMapDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("slice_id")] string? SliceId,
    [property: JsonPropertyName("segment_id")] string? SegmentId,
    [property: JsonPropertyName("cabins")] DuffelSeatCabinDto[]? Cabins
);

public sealed record DuffelSeatCabinDto(
    [property: JsonPropertyName("deck")] int Deck,
    [property: JsonPropertyName("cabin_class")] string? CabinClass,
    [property: JsonPropertyName("rows")] DuffelSeatRowDto[]? Rows
);

public sealed record DuffelSeatRowDto(
    [property: JsonPropertyName("sections")] DuffelSeatSectionDto[]? Sections
);

public sealed record DuffelSeatSectionDto(
    [property: JsonPropertyName("elements")] DuffelSeatElementDto[]? Elements
);

public sealed record DuffelSeatElementDto(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("designator")] string? Designator = null,
    [property: JsonPropertyName("disclosures")] string[]? Disclosures = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("available_services")]
        DuffelSeatServiceDto[]? AvailableServices = null
);

public sealed record DuffelSeatServiceDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("passenger_id")] string? PassengerId,
    [property: JsonPropertyName("total_amount")] string? TotalAmount,
    [property: JsonPropertyName("total_currency")] string? TotalCurrency
);
