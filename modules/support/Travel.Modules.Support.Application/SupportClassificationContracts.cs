using ErrorOr;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Application;

public enum SupportClassificationStatus
{
    Disabled,
    ManualRequired,
    Candidate,
}

public enum SupportClassificationReason
{
    Disabled,
    Ready,
    InputTooLarge,
    Unavailable,
    Uncertain,
    InvalidResponse,
    LowConfidence,
    IdentityMismatch,
    Busy,
}

public sealed record SupportClassificationInput(string SerializedState)
{
    public const string QuestionVersion = "support-category-v1";

    public override string ToString() => nameof(SupportClassificationInput);
}

public sealed record SupportProviderClassification(
    ManualCategory? Category,
    SupportClassificationReason Reason,
    string? ServingProfileId = null,
    int? InputTokens = null
);

public interface ISupportClassificationProvider
{
    bool IsEnabled { get; }
    Task<ErrorOr<SupportProviderClassification>> ClassifyAsync(
        SupportClassificationInput input,
        CancellationToken ct
    );
}

public sealed record RequestSupportClassification(
    Guid CaseId,
    Guid RequestId,
    int ExpectedRevision
);

public sealed record SupportClassificationResult(
    Guid CaseId,
    Guid RequestId,
    int CapturedRevision,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    SupportClassificationStatus Status,
    SupportClassificationReason Reason,
    ManualCategory? Category,
    string? ServingProfileId,
    string? QuestionVersion,
    SupportUrgencyAdvice Urgency
);

public interface ISupportClassificationService
{
    Task<ErrorOr<SupportClassificationResult>> SuggestAsync(
        SupportCaller caller,
        RequestSupportClassification request,
        CancellationToken ct = default
    );
}

public sealed class DisabledSupportClassificationProvider : ISupportClassificationProvider
{
    public bool IsEnabled => false;

    public Task<ErrorOr<SupportProviderClassification>> ClassifyAsync(
        SupportClassificationInput input,
        CancellationToken ct
    ) =>
        Task.FromResult<ErrorOr<SupportProviderClassification>>(
            new SupportProviderClassification(null, SupportClassificationReason.Disabled)
        );
}
