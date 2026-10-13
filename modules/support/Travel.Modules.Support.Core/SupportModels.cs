namespace Travel.Modules.Support.Core;

public enum CaseState
{
    Open,
    WaitingForCustomer,
    Resolved,
}

public enum ManualCategory
{
    Unclassified,
    Payment,
    Ticketing,
    Cancellation,
    Refund,
    FareConditions,
    Ancillaries,
    Technical,
    Other,
}

public enum SupportPriority
{
    Normal,
    High,
}

public enum TriageSource
{
    Default,
    Operator,
}

public enum SupportActorKind
{
    Customer,
    Operator,
}

public enum SupportActionKind
{
    Create,
    CustomerReply,
    ReplyAndReopen,
    OperatorReply,
    Resolve,
    SetTriage,
}

public sealed record SupportOrderLink(Guid OrderId, long StreamVersion, DateTimeOffset VerifiedAt);

public sealed record SupportCase(
    Guid CaseId,
    Guid OwnerId,
    Guid FirstMessageId,
    string ProtectedTitle,
    int TitleWrittenAtRevision,
    CaseState State,
    int Revision,
    ManualCategory Category,
    SupportPriority Priority,
    TriageSource TriageSource,
    SupportOrderLink? OrderLink,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? PurgeAt
)
{
    public bool IsExpired(DateTimeOffset now) => PurgeAt is { } deadline && now >= deadline;

    public override string ToString() => nameof(SupportCase);
}

public sealed record SupportMessage(
    Guid CaseId,
    Guid MessageId,
    Guid ActorId,
    SupportActorKind ActorKind,
    string ProtectedBody,
    int WrittenAtRevision,
    DateTimeOffset CreatedAt
)
{
    public override string ToString() => nameof(SupportMessage);
}

/// <summary>Retained comparison and audit metadata. Never exposed as the wire receipt.</summary>
public sealed record SupportReceipt(
    Guid CaseId,
    Guid ActionId,
    SupportActionKind Kind,
    Guid ActorId,
    SupportActorKind ActorKind,
    int ExpectedRevision,
    int AppliedRevision,
    CaseState AppliedState,
    DateTimeOffset AppliedAt,
    Guid? MessageId,
    Guid? RequestedOrderId,
    ManualCategory? PreviousCategory,
    SupportPriority? PreviousPriority,
    ManualCategory? RequestedCategory,
    SupportPriority? RequestedPriority
);
