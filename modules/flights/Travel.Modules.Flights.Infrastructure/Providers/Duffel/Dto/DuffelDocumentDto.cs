using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelDocumentDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("unique_identifier")] string UniqueIdentifier
);
