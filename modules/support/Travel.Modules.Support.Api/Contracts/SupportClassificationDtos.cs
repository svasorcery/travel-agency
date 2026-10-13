using Travel.Modules.Support.Application;

namespace Travel.Modules.Support.Api.Contracts;

public sealed record RequestSupportClassificationDto(Guid RequestId, int ExpectedRevision);

public sealed record SupportUrgencyAdviceDto(
    string Kind,
    DateTimeOffset? NextScheduledDepartureAt,
    DateTimeOffset? ObservedAt,
    long? StreamVersion,
    string RuleVersion
);

public sealed record SupportClassificationResultDto(
    Guid CaseId,
    Guid RequestId,
    int CapturedRevision,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string Reason,
    string? Category,
    string? Queue,
    string? ServingProfileId,
    string? QuestionVersion,
    SupportUrgencyAdviceDto Urgency
)
{
    public static SupportClassificationResultDto From(SupportClassificationResult result) =>
        new(
            result.CaseId,
            result.RequestId,
            result.CapturedRevision,
            result.GeneratedAt,
            result.ExpiresAt,
            result.Status.ToString(),
            result.Reason.ToString(),
            result.Category?.ToString(),
            result.Category?.ToString(),
            result.ServingProfileId,
            result.QuestionVersion,
            new(
                result.Urgency.Kind.ToString(),
                result.Urgency.NextScheduledDepartureAt,
                result.Urgency.ObservedAt,
                result.Urgency.StreamVersion,
                "support-urgency-v1"
            )
        );
}
