using System.Security.Cryptography;
using System.Text.Json;
using Wolverine.Attributes;

namespace Travel.IntegrationContracts.AI.TripsBriefExtraction;

// Transport identities and public configuration binding only; no Trips domain rules.
public static class TripsBriefMessageIdentity
{
    public const string Requested = "travel.ai.trips-brief.requested";
    public const string Parsed = "travel.ai.trips-brief.parsed";
    public const int Version = 1;
    public const string PromptVersion = "trips-brief-v1";
    public const string Subject = "travel.ai.trips_brief";
    public const string QueueGroup = "travel.ai.trips_brief.workers";
    public const string PrivacyNoticeVersion = "trips-brief-ru-v1";
}

public static class TripsBriefPolicyIdentity
{
    public static string Compute(string processorId, string modelId, string noticeVersion) =>
        Convert.ToHexStringLower(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new[] { processorId, modelId, noticeVersion },
                    new JsonSerializerOptions
                    {
                        Encoder = System
                            .Text
                            .Encodings
                            .Web
                            .JavaScriptEncoder
                            .UnsafeRelaxedJsonEscaping,
                    }
                )
            )
        );
}

public sealed record TripsBriefSpan(int Start, int End);

public sealed record TripsBriefField(
    string State,
    JsonElement? Value,
    TripsBriefSpan[] Evidence,
    JsonElement[] Alternatives
)
{
    public override string ToString() => nameof(TripsBriefField);
}

public sealed record TripsBriefFields(
    TripsBriefField DestinationId,
    TripsBriefField StartDate,
    TripsBriefField EndDate,
    TripsBriefField DurationDays,
    TripsBriefField Adults,
    TripsBriefField Interests,
    TripsBriefField Pace
)
{
    public override string ToString() => nameof(TripsBriefFields);
}

public sealed record TripsBriefIssue(string Code, string[] Slots, TripsBriefSpan[] Evidence)
{
    public override string ToString() => nameof(TripsBriefIssue);
}

[MessageIdentity(TripsBriefMessageIdentity.Requested, Version = TripsBriefMessageIdentity.Version)]
public sealed record TripsBriefRequested(
    Guid CorrelationId,
    string ProcessingPolicyVersion,
    string Text,
    string Locale,
    DateTimeOffset ReferenceInstant,
    string? ReferenceZone,
    DateOnly? ReferenceLocalDate,
    DateTimeOffset Deadline
)
{
    public override string ToString() => nameof(TripsBriefRequested);
}

[MessageIdentity(TripsBriefMessageIdentity.Parsed, Version = TripsBriefMessageIdentity.Version)]
public sealed record TripsBriefParsed(
    Guid CorrelationId,
    string ProcessingPolicyVersion,
    string Status,
    TripsBriefFields? Fields = null,
    TripsBriefIssue[]? Issues = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    decimal? CostUsd = null,
    string? ModelId = null
)
{
    public override string ToString() => nameof(TripsBriefParsed);
}
