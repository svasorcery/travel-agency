using ErrorOr;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Application;

internal static class SupportCaseAccess
{
    public static Error? Guard(SupportCaller caller, SupportActorKind? required = null)
    {
        if (!caller.IsAuthenticated || caller.Subject == Guid.Empty)
            return SupportErrors.Unauthenticated;
        if (!Enum.IsDefined(caller.Kind) || required is { } kind && caller.Kind != kind)
            return SupportErrors.Forbidden;
        if (
            caller.Kind == SupportActorKind.Operator
            && (
                !caller.HasOperatorPermission || caller.AuthorizedParty != "travel-support-operator"
            )
        )
            return SupportErrors.Forbidden;
        return null;
    }

    public static async Task<ErrorOr<SupportCase>> LoadAsync(
        ISupportCaseStore store,
        TimeProvider time,
        SupportCaller caller,
        Guid caseId,
        CancellationToken ct
    )
    {
        if (Guard(caller) is { } denied)
            return denied;
        if (caseId == Guid.Empty)
            return SupportErrors.InvalidInput;
        var loaded = await store.LoadCaseAsync(caseId, ct).WaitAsync(ct);
        if (loaded.IsError)
            return SupportErrors.Unavailable;
        if (
            loaded.Value is not { } row
            || caller.Kind == SupportActorKind.Customer && row.OwnerId != caller.Subject
            || row.IsExpired(time.GetUtcNow())
        )
            return SupportErrors.NotFound;
        return row.CaseId != caseId || !ValidCase(row) ? SupportErrors.Unavailable : row;
    }

    public static bool ValidMessage(SupportMessage message, Guid caseId) =>
        message.CaseId == caseId
        && message.MessageId != Guid.Empty
        && message.ActorId != Guid.Empty
        && Enum.IsDefined(message.ActorKind)
        && message.WrittenAtRevision > 0
        && !string.IsNullOrWhiteSpace(message.ProtectedBody);

    public static bool ValidCase(SupportCase row) =>
        row.CaseId != Guid.Empty
        && row.OwnerId != Guid.Empty
        && row.FirstMessageId != Guid.Empty
        && row.Revision > 0
        && row.TitleWrittenAtRevision == 1
        && !string.IsNullOrWhiteSpace(row.ProtectedTitle)
        && Enum.IsDefined(row.State)
        && Enum.IsDefined(row.Category)
        && Enum.IsDefined(row.Priority)
        && Enum.IsDefined(row.TriageSource)
        && (
            row.State == CaseState.Resolved
                ? row.ResolvedAt is not null && row.PurgeAt == row.ResolvedAt.Value.AddDays(90)
                : row.ResolvedAt is null && row.PurgeAt is null
        )
        && (
            row.OrderLink is null
            || row.OrderLink.OrderId != Guid.Empty && row.OrderLink.StreamVersion > 0
        );
}
