using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Infrastructure.Persistence;

public sealed class SupportCaseEntity
{
    public Guid CaseId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid FirstMessageId { get; set; }
    public string ProtectedTitle { get; set; } = "";
    public int TitleWrittenAtRevision { get; set; }
    public CaseState State { get; set; }
    public int Revision { get; set; }
    public ManualCategory Category { get; set; }
    public SupportPriority Priority { get; set; }
    public TriageSource TriageSource { get; set; }
    public Guid? OrderId { get; set; }
    public long? OrderStreamVersion { get; set; }
    public DateTimeOffset? OrderVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? PurgeAt { get; set; }

    public SupportCase ToRecord() =>
        new(
            CaseId,
            OwnerId,
            FirstMessageId,
            ProtectedTitle,
            TitleWrittenAtRevision,
            State,
            Revision,
            Category,
            Priority,
            TriageSource,
            OrderId is { } id
            && OrderStreamVersion is { } version
            && OrderVerifiedAt is { } verified
                ? new(id, version, verified)
                : null,
            CreatedAt,
            UpdatedAt,
            ResolvedAt,
            PurgeAt
        );

    public static SupportCaseEntity From(SupportCase value) =>
        new()
        {
            CaseId = value.CaseId,
            OwnerId = value.OwnerId,
            FirstMessageId = value.FirstMessageId,
            ProtectedTitle = value.ProtectedTitle,
            TitleWrittenAtRevision = value.TitleWrittenAtRevision,
            State = value.State,
            Revision = value.Revision,
            Category = value.Category,
            Priority = value.Priority,
            TriageSource = value.TriageSource,
            OrderId = value.OrderLink?.OrderId,
            OrderStreamVersion = value.OrderLink?.StreamVersion,
            OrderVerifiedAt = value.OrderLink?.VerifiedAt,
            CreatedAt = value.CreatedAt,
            UpdatedAt = value.UpdatedAt,
            ResolvedAt = value.ResolvedAt,
            PurgeAt = value.PurgeAt,
        };
}

public sealed class SupportMessageEntity
{
    public Guid CaseId { get; set; }
    public Guid MessageId { get; set; }
    public Guid ActorId { get; set; }
    public SupportActorKind ActorKind { get; set; }
    public string ProtectedBody { get; set; } = "";
    public int WrittenAtRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SupportMessage ToRecord() =>
        new(CaseId, MessageId, ActorId, ActorKind, ProtectedBody, WrittenAtRevision, CreatedAt);

    public static SupportMessageEntity From(SupportMessage value) =>
        new()
        {
            CaseId = value.CaseId,
            MessageId = value.MessageId,
            ActorId = value.ActorId,
            ActorKind = value.ActorKind,
            ProtectedBody = value.ProtectedBody,
            WrittenAtRevision = value.WrittenAtRevision,
            CreatedAt = value.CreatedAt,
        };
}

public sealed class SupportReceiptEntity
{
    public Guid CaseId { get; set; }
    public Guid ActionId { get; set; }
    public SupportActionKind Kind { get; set; }
    public Guid ActorId { get; set; }
    public SupportActorKind ActorKind { get; set; }
    public int ExpectedRevision { get; set; }
    public int AppliedRevision { get; set; }
    public CaseState AppliedState { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? RequestedOrderId { get; set; }
    public ManualCategory? PreviousCategory { get; set; }
    public SupportPriority? PreviousPriority { get; set; }
    public ManualCategory? RequestedCategory { get; set; }
    public SupportPriority? RequestedPriority { get; set; }

    public SupportReceipt ToRecord() =>
        new(
            CaseId,
            ActionId,
            Kind,
            ActorId,
            ActorKind,
            ExpectedRevision,
            AppliedRevision,
            AppliedState,
            AppliedAt,
            MessageId,
            RequestedOrderId,
            PreviousCategory,
            PreviousPriority,
            RequestedCategory,
            RequestedPriority
        );

    public static SupportReceiptEntity From(SupportReceipt value) =>
        new()
        {
            CaseId = value.CaseId,
            ActionId = value.ActionId,
            Kind = value.Kind,
            ActorId = value.ActorId,
            ActorKind = value.ActorKind,
            ExpectedRevision = value.ExpectedRevision,
            AppliedRevision = value.AppliedRevision,
            AppliedState = value.AppliedState,
            AppliedAt = value.AppliedAt,
            MessageId = value.MessageId,
            RequestedOrderId = value.RequestedOrderId,
            PreviousCategory = value.PreviousCategory,
            PreviousPriority = value.PreviousPriority,
            RequestedCategory = value.RequestedCategory,
            RequestedPriority = value.RequestedPriority,
        };
}
