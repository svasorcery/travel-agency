using Wolverine.Attributes;

namespace Travel.IntegrationContracts.AI.FareSummary;

public static class FareSummaryMessageIdentity
{
    public const string Requested = "travel.ai.fare-summary.requested";
    public const string Planned = "travel.ai.fare-summary.planned";
    public const int Version = 1;
    public const string PromptVersion = "fare-summary-v1";
    public const string Subject = "travel.ai.fare_summary";
    public const string QueueGroup = "travel.ai.fare_summary.workers";
}

public sealed record FareSummaryScope(
    string Kind,
    int? Leg = null,
    int? Segment = null,
    int? Adult = null
);

// Controlled claim codes and primitive facts only. Supplier labels, references,
// booking IDs, passenger profiles and credentials have no field in this contract.
public sealed record FareSummaryFact(
    string Id,
    string Section,
    string Kind,
    string State,
    string? Amount,
    string? Currency,
    int? Quantity,
    FareSummaryScope[] Scopes
);

[MessageIdentity(
    FareSummaryMessageIdentity.Requested,
    Version = FareSummaryMessageIdentity.Version
)]
public sealed record FareSummaryRequested(
    Guid CorrelationId,
    string Fingerprint,
    string PromptVersion,
    string Locale,
    DateTimeOffset Deadline,
    FareSummaryFact[] Facts
);

public sealed record FareSummaryPlanGroup(string[] FactIds);

public sealed record FareSummaryPlanSection(string Kind, FareSummaryPlanGroup[] Groups);

[MessageIdentity(FareSummaryMessageIdentity.Planned, Version = FareSummaryMessageIdentity.Version)]
public sealed record FareSummaryPlanned(
    Guid CorrelationId,
    string Fingerprint,
    string PromptVersion,
    string Locale,
    string Status,
    FareSummaryPlanSection[]? Sections = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    decimal? CostUsd = null,
    string? ModelId = null
);
