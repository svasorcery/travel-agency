using Wolverine.Attributes;

namespace Travel.IntegrationContracts.AI.NlSearch;

public static class FlightSearchPreviewMessageIdentity
{
    public const string Requested = "travel.ai.flight-search-preview.requested";
    public const string Parsed = "travel.ai.flight-search-preview.parsed";
    public const int Version = 1;
    public const string Subject = "travel.ai.flight_search_preview";
    public const string QueueGroup = "travel.ai.flight_search_preview.workers";
    public const string ChatClientKey = "flight-search-preview";
}

[MessageIdentity(
    FlightSearchPreviewMessageIdentity.Requested,
    Version = FlightSearchPreviewMessageIdentity.Version
)]
public sealed record FlightSearchPreviewRequested(
    string Query,
    DateOnly ReferenceDate,
    Guid CorrelationId
);

[MessageIdentity(
    FlightSearchPreviewMessageIdentity.Parsed,
    Version = FlightSearchPreviewMessageIdentity.Version
)]
public sealed record FlightSearchPreviewParsed(
    Guid CorrelationId,
    FlightSearchPreviewFragments? Fragments
);

public sealed record FlightSearchPreviewFragments(
    string? Origin,
    string? Destination,
    string? Departure,
    string? Return,
    string? Party,
    string? TripType,
    string[] Restrictions
);
