using ErrorOr;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Application;

public sealed record SupportCaller(
    Guid Subject,
    SupportActorKind Kind,
    bool IsAuthenticated,
    bool HasOperatorPermission = false,
    string? AuthorizedParty = null
);

public sealed record SupportTextBinding(
    Guid OwnerId,
    Guid CaseId,
    Guid? MessageId,
    int WrittenAtRevision
);

public sealed record CreateSupportCase(
    Guid CaseId,
    Guid MessageId,
    string Title,
    string Body,
    Guid? OrderId = null
)
{
    public override string ToString() => nameof(CreateSupportCase);
}

public sealed record ReplyToSupportCase(
    Guid CaseId,
    Guid MessageId,
    string Body,
    int ExpectedRevision,
    SupportActionKind Kind
)
{
    public override string ToString() => nameof(ReplyToSupportCase);
}

public sealed record ActOnSupportCase(
    Guid CaseId,
    Guid ActionId,
    int ExpectedRevision,
    SupportActionKind Kind,
    ManualCategory? Category = null,
    SupportPriority? Priority = null
);

public sealed record SupportCaseFilters(
    CaseState? State = null,
    ManualCategory? Category = null,
    SupportPriority? Priority = null
);

public sealed record SupportCasePosition(DateTimeOffset CreatedAt, Guid CaseId);

public sealed record SupportMessagePosition(int WrittenAtRevision, Guid MessageId);

public sealed record SupportCaseQuery(
    Guid? OwnerId,
    SupportCaseFilters Filters,
    SupportCasePosition? After,
    DateTimeOffset Now,
    int Limit = 21
);

public sealed record SupportMessageQuery(
    Guid CaseId,
    SupportMessagePosition? After,
    int Limit = 21
);

public sealed record SupportCaseView(
    Guid CaseId,
    Guid OwnerId,
    string Title,
    CaseState State,
    int Revision,
    ManualCategory Category,
    SupportPriority Priority,
    TriageSource TriageSource,
    Guid? OrderId,
    DateTimeOffset? OrderVerifiedAt,
    long? OrderStreamVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? PurgeAt
)
{
    public override string ToString() => nameof(SupportCaseView);
}

public sealed record SupportMessageView(
    Guid CaseId,
    Guid MessageId,
    SupportActorKind ActorKind,
    string Body,
    int WrittenAtRevision,
    DateTimeOffset CreatedAt
)
{
    public override string ToString() => nameof(SupportMessageView);
}

public sealed record SupportActionReceipt(
    Guid CaseId,
    Guid ActionId,
    SupportActionKind Kind,
    int AppliedRevision,
    CaseState AppliedState,
    DateTimeOffset AppliedAt,
    Guid? MessageId
);

public sealed record SupportMutationResult(SupportActionReceipt Receipt, bool IsReplay);

public sealed record SupportCasePage(IReadOnlyList<SupportCaseView> Items, string? NextCursor)
{
    public override string ToString() => nameof(SupportCasePage);
}

public sealed record SupportMessagePage(
    Guid CaseId,
    IReadOnlyList<SupportMessageView> Items,
    string? NextCursor
)
{
    public override string ToString() => nameof(SupportMessagePage);
}

/// <summary>Each write is one synchronous-request transaction, including its receipt and optional immutable message.</summary>
public interface ISupportCaseStore
{
    Task<ErrorOr<SupportCase?>> LoadCaseAsync(Guid caseId, CancellationToken cancellationToken);
    Task<ErrorOr<SupportReceipt?>> LoadReceiptAsync(
        Guid caseId,
        Guid actionId,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<SupportMessage?>> LoadMessageAsync(
        Guid caseId,
        Guid messageId,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<IReadOnlyList<SupportCase>>> ListCasesAsync(
        SupportCaseQuery query,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<IReadOnlyList<SupportMessage>>> ListMessagesAsync(
        SupportMessageQuery query,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<Success>> TryCreateAsync(
        SupportCase supportCase,
        SupportMessage message,
        SupportReceipt receipt,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<Success>> TryUpdateAsync(
        int expectedRevision,
        SupportCase supportCase,
        SupportMessage? message,
        SupportReceipt receipt,
        CancellationToken cancellationToken
    );
}

public interface ISupportTextProtector
{
    Task<ErrorOr<string>> ProtectAsync(
        string plaintext,
        SupportTextBinding binding,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<string>> UnprotectAsync(
        string envelope,
        SupportTextBinding binding,
        CancellationToken cancellationToken
    );
}

public interface ISupportCaseService
{
    Task<ErrorOr<SupportMutationResult>> CreateAsync(
        SupportCaller caller,
        CreateSupportCase command,
        CancellationToken cancellationToken = default
    );
    Task<ErrorOr<SupportMutationResult>> ReplyAsync(
        SupportCaller caller,
        ReplyToSupportCase command,
        CancellationToken cancellationToken = default
    );
    Task<ErrorOr<SupportMutationResult>> ActAsync(
        SupportCaller caller,
        ActOnSupportCase command,
        CancellationToken cancellationToken = default
    );
    Task<ErrorOr<SupportCaseView>> GetCaseAsync(
        SupportCaller caller,
        Guid caseId,
        CancellationToken cancellationToken = default
    );
    Task<ErrorOr<SupportCasePage>> ListCasesAsync(
        SupportCaller caller,
        SupportCaseFilters? filters = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );
    Task<ErrorOr<SupportMessagePage>> ListMessagesAsync(
        SupportCaller caller,
        Guid caseId,
        string? cursor = null,
        CancellationToken cancellationToken = default
    );
    Task<ErrorOr<SupportActionReceipt>> GetReceiptAsync(
        SupportCaller caller,
        Guid caseId,
        Guid actionId,
        CancellationToken cancellationToken = default
    );
}
