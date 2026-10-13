using ErrorOr;

namespace Travel.Modules.Support.Core;

public static class SupportWorkflow
{
    public static ErrorOr<SupportCase> Apply(
        SupportCase current,
        SupportActorKind actor,
        SupportActionKind action,
        int expectedRevision,
        DateTimeOffset now,
        ManualCategory? category = null,
        SupportPriority? priority = null
    )
    {
        if (current.IsExpired(now))
            return SupportErrors.NotFound;
        if (
            expectedRevision <= 0
            || !Enum.IsDefined(actor)
            || !Enum.IsDefined(action)
            || !Enum.IsDefined(current.State)
        )
            return SupportErrors.InvalidInput;
        if (current.Revision != expectedRevision || current.Revision == int.MaxValue)
            return SupportErrors.Conflict;
        var customerAction =
            action is SupportActionKind.CustomerReply or SupportActionKind.ReplyAndReopen;
        var operatorAction =
            action
            is SupportActionKind.OperatorReply
                or SupportActionKind.Resolve
                or SupportActionKind.SetTriage;
        if (actor == SupportActorKind.Customer ? !customerAction : !operatorAction)
            return SupportErrors.Forbidden;
        if (action == SupportActionKind.SetTriage)
        {
            if (
                category is null
                || priority is null
                || !Enum.IsDefined(category.Value)
                || !Enum.IsDefined(priority.Value)
            )
                return SupportErrors.InvalidInput;
            if (
                current.Category == category
                && current.Priority == priority
                && current.TriageSource == TriageSource.Operator
            )
                return SupportErrors.Conflict;
            return current with
            {
                Category = category.Value,
                Priority = priority.Value,
                TriageSource = TriageSource.Operator,
                Revision = current.Revision + 1,
                UpdatedAt = now,
            };
        }
        if (category is not null || priority is not null)
            return SupportErrors.InvalidInput;
        if (action == SupportActionKind.ReplyAndReopen)
        {
            if (current.State != CaseState.Resolved)
                return SupportErrors.Conflict;
            return current with
            {
                State = CaseState.Open,
                Revision = current.Revision + 1,
                UpdatedAt = now,
                ResolvedAt = null,
                PurgeAt = null,
            };
        }
        if (current.State == CaseState.Resolved)
            return SupportErrors.Conflict;
        var next =
            action == SupportActionKind.Resolve ? CaseState.Resolved
            : action == SupportActionKind.OperatorReply ? CaseState.WaitingForCustomer
            : CaseState.Open;
        return current with
        {
            State = next,
            Revision = current.Revision + 1,
            UpdatedAt = now,
            ResolvedAt = action == SupportActionKind.Resolve ? now : null,
            PurgeAt = action == SupportActionKind.Resolve ? now.AddDays(90) : null,
        };
    }
}
