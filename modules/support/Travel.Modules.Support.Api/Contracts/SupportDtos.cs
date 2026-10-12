using Travel.Modules.Support.Application;

namespace Travel.Modules.Support.Api.Contracts;

public sealed record CreateSupportCaseDto(
    Guid MessageId,
    string Title,
    string Body,
    Guid? OrderId = null
)
{
    public override string ToString() => nameof(CreateSupportCaseDto);
}

public sealed record ReplySupportCaseDto(string Body, int ExpectedRevision, string Kind)
{
    public override string ToString() => nameof(ReplySupportCaseDto);
}

public sealed record ActSupportCaseDto(
    string Kind,
    int ExpectedRevision,
    string? Category = null,
    string? Priority = null
);

public sealed record SupportCaseDto(
    Guid CaseId,
    Guid OwnerId,
    string Title,
    string State,
    int Revision,
    string Category,
    string Priority,
    string TriageSource,
    Guid? OrderId,
    DateTimeOffset? OrderVerifiedAt,
    long? OrderStreamVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? PurgeAt
)
{
    public override string ToString() => nameof(SupportCaseDto);

    public static SupportCaseDto From(SupportCaseView value) =>
        new(
            value.CaseId,
            value.OwnerId,
            value.Title,
            value.State.ToString(),
            value.Revision,
            value.Category.ToString(),
            value.Priority.ToString(),
            value.TriageSource.ToString(),
            value.OrderId,
            value.OrderVerifiedAt,
            value.OrderStreamVersion,
            value.CreatedAt,
            value.UpdatedAt,
            value.ResolvedAt,
            value.PurgeAt
        );
}

public sealed record SupportMessageDto(
    Guid CaseId,
    Guid MessageId,
    string ActorKind,
    string Body,
    int WrittenAtRevision,
    DateTimeOffset CreatedAt
)
{
    public override string ToString() => nameof(SupportMessageDto);

    public static SupportMessageDto From(SupportMessageView value) =>
        new(
            value.CaseId,
            value.MessageId,
            value.ActorKind.ToString(),
            value.Body,
            value.WrittenAtRevision,
            value.CreatedAt
        );
}

public sealed record SupportReceiptDto(
    Guid CaseId,
    Guid ActionId,
    string Kind,
    int AppliedRevision,
    string AppliedState,
    DateTimeOffset AppliedAt,
    Guid? MessageId
)
{
    public static SupportReceiptDto From(SupportActionReceipt value) =>
        new(
            value.CaseId,
            value.ActionId,
            value.Kind.ToString(),
            value.AppliedRevision,
            value.AppliedState.ToString(),
            value.AppliedAt,
            value.MessageId
        );
}

public sealed record SupportCasePageDto(IReadOnlyList<SupportCaseDto> Items, string? NextCursor)
{
    public override string ToString() => nameof(SupportCasePageDto);
}

public sealed record SupportMessagePageDto(
    Guid CaseId,
    IReadOnlyList<SupportMessageDto> Items,
    string? NextCursor
)
{
    public override string ToString() => nameof(SupportMessagePageDto);
}
