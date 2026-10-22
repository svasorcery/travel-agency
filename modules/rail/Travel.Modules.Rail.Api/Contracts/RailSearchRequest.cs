using System.Text.Json.Serialization;

namespace Travel.Modules.Rail.Api.Contracts;

// Public input metadata for the same fields checked by RailEndpoints.Strict.
// The endpoint retains its bounded manual reader; this is not supplier wire data
// or a replacement for Core's factory-owned opaque reference invariants.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RailSearchRequest(
    RailStationReferenceRequest From,
    RailStationReferenceRequest To,
    string CatalogueGeneration,
    string DepartureDate,
    string Transport
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RailStationReferenceRequest(string Source, string Code);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RailJourneyDetailsRequestMetadata(
    RailSearchRequest Search,
    string ExpectedProfileEpoch,
    RailJourneySelectionMetadata Selection
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RailJourneySelectionMetadata(
    string Key,
    string Transport,
    string Departure,
    string Arrival
);
