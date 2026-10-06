using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Cancellation;

public enum CancellationPhase
{
    None = 0,
    Preparing = 1,
    TermsReady = 2,
    Accepted = 3,
    DispatchClaimed = 4,
    Unknown = 5,
    ManualReviewRequired = 6,
    Succeeded = 7,
    Rejected = 8,
    Abandoned = 9,
    Expired = 10,
    UnsupportedTerms = 11,
}

public enum CancellationOutcome
{
    None = 0,
    Succeeded = 1,
    Rejected = 2,
    Unknown = 3,
}

public enum CancellationUnknownStage
{
    None = 0,
    Preparation = 1,
    Confirmation = 2,
}

public enum CancellationDecisionKind
{
    Rejected = 0,
    Allowed = 1,
    NoOp = 2,
}

public sealed record CancellationDecision(
    CancellationDecisionKind Kind,
    CancellationReason Reason,
    EquatableArray<IDomainEvent> Events,
    Guid? ResolvedOperationId = null
);

public sealed record CancellationOperation
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public long Revision { get; init; }
    public string ProviderOrderRef { get; init; } = "";
    public string ItineraryPartyHash { get; init; } = "";
    public string PrepareFingerprint { get; init; } = "";
    public string? ConsentFingerprint { get; init; }
    public Guid PreparationAdmissionId { get; init; }
    public Guid? ConfirmationAdmissionId { get; init; }
    public DateTimeOffset AdmittedAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; init; }
    public CancellationPhase Phase { get; init; }
    public CancellationOutcome Outcome { get; init; }
    public CancellationUnknownStage UnknownStage { get; init; }
    public CancellationTerms? Terms { get; init; }
    public bool PreparationCompleted { get; init; }
    public string? ProviderCancellationRef { get; init; }
    public DateTimeOffset? PreparationDispatchedAt { get; init; }
    public DateTimeOffset? ConfirmationDispatchedAt { get; init; }
    public Guid? DispatchOwnerInstanceId { get; init; }
    public RecoverySchedule? Recovery { get; init; }
    public bool HadUnknown { get; init; }
    public bool HadPreparationUnknown { get; init; }
    public bool HadConfirmationUnknown { get; init; }
    public Guid? ActiveReadId { get; init; }
    public CancellationEvidence? Evidence { get; init; }
    public CancellationResolutionSource ResolutionSource { get; init; }
    public CancellationReason Reason { get; init; }
    public Guid? ConsumedRefreshRequestId { get; init; }
    public Guid? LastRefreshRequestId { get; init; }
    public string? LastRefreshFingerprint { get; init; }
    public bool IsTerminal =>
        Phase
            is CancellationPhase.Succeeded
                or CancellationPhase.Rejected
                or CancellationPhase.Abandoned
                or CancellationPhase.Expired;

    public override string ToString() => nameof(CancellationOperation);
}
