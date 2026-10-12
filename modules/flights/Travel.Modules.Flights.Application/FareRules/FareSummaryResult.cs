using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.FareRules;

public sealed record FareSummaryLine(string[] FactIds, string Scope, string Text);

public sealed record FareSummarySection(string Kind, FareSummaryLine[] Lines);

public sealed record FareSummaryResult(
    int SchemaVersion,
    Guid AggregateId,
    Guid? OfferId,
    Guid? QuoteRevision,
    string? Fingerprint,
    string State,
    string? Reason,
    string Provenance,
    DateTimeOffset? SourceObservedAt,
    DateTimeOffset? OfferExpiresAt,
    DateTimeOffset? FirstDepartureAt,
    DateTimeOffset ServerObservedAt,
    int PassengerCount,
    int[] SegmentCounts,
    bool AiAvailable,
    string AiStatus,
    string Presentation,
    FareCondition? ChangeBeforeDeparture,
    FareCondition? RefundBeforeDeparture,
    FareSummarySection[] Sections,
    FareLegFact[] Legs,
    FareSegmentFact[] Passengers,
    string[] MissingInformation,
    string[] Notices
);
