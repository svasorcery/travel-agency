using ErrorOr;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Application;

public sealed class SupportCaseService(
    ISupportCaseStore store,
    ISupportTextProtector protector,
    IFlightsSupportOrderAccess orders,
    TimeProvider timeProvider
) : ISupportCaseService
{
    public async Task<ErrorOr<SupportMutationResult>> CreateAsync(
        SupportCaller caller,
        CreateSupportCase command,
        CancellationToken cancellationToken = default
    )
    {
        var guard = Guard(caller, SupportActorKind.Customer);
        if (guard is { } denied)
            return denied;
        if (
            command.CaseId == Guid.Empty
            || command.MessageId == Guid.Empty
            || command.OrderId == Guid.Empty
            || SupportTitle.Create(command.Title).IsError
            || SupportBody.Create(command.Body).IsError
        )
            return SupportErrors.InvalidInput;
        var loaded = await store.LoadCaseAsync(command.CaseId, cancellationToken);
        if (loaded.IsError)
            return SupportErrors.Unavailable;
        if (loaded.Value is { } existing)
            return await ReplayCreateAsync(caller, command, existing, cancellationToken);
        SupportOrderLink? link = null;
        if (command.OrderId is { } orderId)
        {
            var proof = await orders.VerifyOwnershipAsync(
                caller.Subject,
                orderId,
                cancellationToken
            );
            if (proof.IsError)
                return proof.FirstError.Type == ErrorType.NotFound
                    ? SupportErrors.OrderLinkNotFound
                    : SupportErrors.OrderLinkUnavailable;
            if (proof.Value.OwnerId != caller.Subject || proof.Value.OrderId != orderId)
                return SupportErrors.OrderLinkNotFound;
            if (proof.Value.StreamVersion <= 0 || proof.Value.VerifiedAt.Offset != TimeSpan.Zero)
                return SupportErrors.OrderLinkUnavailable;
            link = new(orderId, proof.Value.StreamVersion, proof.Value.VerifiedAt);
        }
        var now = WriteNow();
        var title = await protector.ProtectAsync(
            command.Title,
            new(caller.Subject, command.CaseId, null, 1),
            cancellationToken
        );
        if (title.IsError || string.IsNullOrWhiteSpace(title.Value))
            return SupportErrors.Unavailable;
        var body = await protector.ProtectAsync(
            command.Body,
            new(caller.Subject, command.CaseId, command.MessageId, 1),
            cancellationToken
        );
        if (body.IsError || string.IsNullOrWhiteSpace(body.Value))
            return SupportErrors.Unavailable;
        var row = new SupportCase(
            command.CaseId,
            caller.Subject,
            command.MessageId,
            title.Value,
            1,
            CaseState.Open,
            1,
            ManualCategory.Unclassified,
            SupportPriority.Normal,
            TriageSource.Default,
            link,
            now,
            now,
            null,
            null
        );
        var message = new SupportMessage(
            command.CaseId,
            command.MessageId,
            caller.Subject,
            SupportActorKind.Customer,
            body.Value,
            1,
            now
        );
        var receipt = new SupportReceipt(
            command.CaseId,
            command.MessageId,
            SupportActionKind.Create,
            caller.Subject,
            SupportActorKind.Customer,
            0,
            1,
            CaseState.Open,
            now,
            command.MessageId,
            command.OrderId,
            null,
            null,
            null,
            null
        );
        var written = await store.TryCreateAsync(row, message, receipt, cancellationToken);
        if (!written.IsError)
            return new SupportMutationResult(View(receipt), false);
        if (written.FirstError.Type != ErrorType.Conflict)
            return SupportErrors.Unavailable;
        // Reconcile a unique-key race by retained reads; never dispatch another write.
        loaded = await store.LoadCaseAsync(command.CaseId, cancellationToken);
        if (loaded.IsError)
            return SupportErrors.Unavailable;
        return loaded.Value is { } committed
            ? await ReplayCreateAsync(caller, command, committed, cancellationToken)
            : SupportErrors.Conflict;
    }

    public async Task<ErrorOr<SupportMutationResult>> ReplyAsync(
        SupportCaller caller,
        ReplyToSupportCase command,
        CancellationToken cancellationToken = default
    )
    {
        var guard = Guard(caller);
        if (guard is { } denied)
            return denied;
        var permitted =
            caller.Kind == SupportActorKind.Customer
                ? command.Kind
                    is SupportActionKind.CustomerReply
                        or SupportActionKind.ReplyAndReopen
                : command.Kind == SupportActionKind.OperatorReply;
        if (!permitted)
            return SupportErrors.Forbidden;
        if (
            command.CaseId == Guid.Empty
            || command.MessageId == Guid.Empty
            || command.ExpectedRevision <= 0
            || SupportBody.Create(command.Body).IsError
        )
            return SupportErrors.InvalidInput;
        var loaded = await LoadAuthorizedAsync(caller, command.CaseId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        var previous = loaded.Value;
        var retained = await store.LoadReceiptAsync(
            command.CaseId,
            command.MessageId,
            cancellationToken
        );
        if (previous.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (retained.IsError)
            return SupportErrors.Unavailable;
        if (retained.Value is { } existing)
            return await ReplayReplyAsync(caller, command, previous, existing, cancellationToken);
        var now = WriteNow();
        var decision = SupportWorkflow.Apply(
            previous,
            caller.Kind,
            command.Kind,
            command.ExpectedRevision,
            now
        );
        if (decision.IsError)
            return decision.Errors;
        var next = decision.Value;
        var body = await protector.ProtectAsync(
            command.Body,
            new(previous.OwnerId, command.CaseId, command.MessageId, next.Revision),
            cancellationToken
        );
        if (previous.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (body.IsError || string.IsNullOrWhiteSpace(body.Value))
            return SupportErrors.Unavailable;
        var message = new SupportMessage(
            command.CaseId,
            command.MessageId,
            caller.Subject,
            caller.Kind,
            body.Value,
            next.Revision,
            now
        );
        var receipt = NewReceipt(
            caller,
            next,
            command.MessageId,
            command.Kind,
            command.ExpectedRevision,
            command.MessageId,
            null,
            null,
            null,
            null
        );
        var written = await store.TryUpdateAsync(
            command.ExpectedRevision,
            next,
            message,
            receipt,
            cancellationToken
        );
        if (!written.IsError)
            return new SupportMutationResult(View(receipt), false);
        if (written.FirstError.Type != ErrorType.Conflict)
            return SupportErrors.Unavailable;
        loaded = await LoadAuthorizedAsync(caller, command.CaseId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        retained = await store.LoadReceiptAsync(
            command.CaseId,
            command.MessageId,
            cancellationToken
        );
        if (retained.IsError)
            return SupportErrors.Unavailable;
        return retained.Value is { } raced
            ? await ReplayReplyAsync(caller, command, loaded.Value, raced, cancellationToken)
            : SupportErrors.Conflict;
    }

    public async Task<ErrorOr<SupportMutationResult>> ActAsync(
        SupportCaller caller,
        ActOnSupportCase command,
        CancellationToken cancellationToken = default
    )
    {
        var guard = Guard(caller, SupportActorKind.Operator);
        if (guard is { } denied)
            return denied;
        if (
            command.CaseId == Guid.Empty
            || command.ActionId == Guid.Empty
            || command.ExpectedRevision <= 0
            || command.Kind is not (SupportActionKind.Resolve or SupportActionKind.SetTriage)
        )
            return SupportErrors.InvalidInput;
        if (
            command.Kind == SupportActionKind.SetTriage
                ? command.Category is null
                    || command.Priority is null
                    || !Enum.IsDefined(command.Category.Value)
                    || !Enum.IsDefined(command.Priority.Value)
                : command.Category is not null || command.Priority is not null
        )
            return SupportErrors.InvalidInput;
        var loaded = await LoadAuthorizedAsync(caller, command.CaseId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        var previous = loaded.Value;
        var retained = await store.LoadReceiptAsync(
            command.CaseId,
            command.ActionId,
            cancellationToken
        );
        if (previous.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (retained.IsError)
            return SupportErrors.Unavailable;
        if (retained.Value is { } existing)
            return ReplayAction(caller, command, existing);
        var decision = SupportWorkflow.Apply(
            previous,
            caller.Kind,
            command.Kind,
            command.ExpectedRevision,
            WriteNow(),
            command.Category,
            command.Priority
        );
        if (decision.IsError)
            return decision.Errors;
        var receipt = NewReceipt(
            caller,
            decision.Value,
            command.ActionId,
            command.Kind,
            command.ExpectedRevision,
            null,
            command.Kind == SupportActionKind.SetTriage ? previous.Category : null,
            command.Kind == SupportActionKind.SetTriage ? previous.Priority : null,
            command.Category,
            command.Priority
        );
        var written = await store.TryUpdateAsync(
            command.ExpectedRevision,
            decision.Value,
            null,
            receipt,
            cancellationToken
        );
        if (!written.IsError)
            return new SupportMutationResult(View(receipt), false);
        if (written.FirstError.Type != ErrorType.Conflict)
            return SupportErrors.Unavailable;
        loaded = await LoadAuthorizedAsync(caller, command.CaseId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        retained = await store.LoadReceiptAsync(
            command.CaseId,
            command.ActionId,
            cancellationToken
        );
        if (loaded.Value.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (retained.IsError)
            return SupportErrors.Unavailable;
        return retained.Value is { } raced
            ? ReplayAction(caller, command, raced)
            : SupportErrors.Conflict;
    }

    public async Task<ErrorOr<SupportCaseView>> GetCaseAsync(
        SupportCaller caller,
        Guid caseId,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadAuthorizedAsync(caller, caseId, cancellationToken);
        return loaded.IsError
            ? loaded.Errors
            : await ReadCaseAsync(loaded.Value, cancellationToken);
    }

    public async Task<ErrorOr<SupportActionReceipt>> GetReceiptAsync(
        SupportCaller caller,
        Guid caseId,
        Guid actionId,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadAuthorizedAsync(caller, caseId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        if (actionId == Guid.Empty)
            return SupportErrors.InvalidInput;
        var retained = await store.LoadReceiptAsync(caseId, actionId, cancellationToken);
        if (retained.IsError)
            return SupportErrors.Unavailable;
        if (
            retained.Value is not { } receipt
            || !ReceiptActorMatches(receipt, caller)
            || loaded.Value.IsExpired(timeProvider.GetUtcNow())
        )
            return SupportErrors.NotFound;
        if (receipt.CaseId != caseId || receipt.ActionId != actionId || !ValidReceipt(receipt))
            return SupportErrors.Unavailable;
        return View(receipt);
    }

    public async Task<ErrorOr<SupportCasePage>> ListCasesAsync(
        SupportCaller caller,
        SupportCaseFilters? filters = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var guard = Guard(caller);
        if (guard is { } denied)
            return denied;
        filters ??= new();
        if (!SupportCursors.ValidFilters(filters))
            return SupportErrors.InvalidInput;
        var position = SupportCursors.ReadCase(cursor, filters);
        if (position.IsError)
            return position.Errors;
        var rows = await store.ListCasesAsync(
            new(
                caller.Kind == SupportActorKind.Customer ? caller.Subject : null,
                filters,
                position.Value,
                timeProvider.GetUtcNow()
            ),
            cancellationToken
        );
        if (rows.IsError || rows.Value is null || rows.Value.Count > 21)
            return SupportErrors.Unavailable;
        var ids = new HashSet<Guid>();
        var last = position.Value;
        foreach (var row in rows.Value)
        {
            if (
                !ValidCase(row)
                || caller.Kind == SupportActorKind.Customer && row.OwnerId != caller.Subject
                || !ids.Add(row.CaseId)
                || !SupportCursors.After(row, last)
                || !SupportCursors.Matches(row, filters)
            )
                return SupportErrors.Unavailable;
            last = new(row.CreatedAt, row.CaseId);
        }
        var items = new List<SupportCaseView>();
        foreach (var row in rows.Value.Take(20))
        {
            if (row.IsExpired(timeProvider.GetUtcNow()))
                continue;
            var view = await ReadCaseAsync(row, cancellationToken);
            if (view.IsError)
            {
                if (view.FirstError.Type == ErrorType.NotFound)
                    continue;
                return view.Errors;
            }
            items.Add(view.Value);
        }
        var completedAt = timeProvider.GetUtcNow();
        items.RemoveAll(item => item.PurgeAt is { } deadline && completedAt >= deadline);
        return new SupportCasePage(
            items.AsReadOnly(),
            rows.Value.Count > 20
                ? SupportCursors.WriteCase(
                    filters,
                    new(rows.Value[19].CreatedAt, rows.Value[19].CaseId)
                )
                : null
        );
    }

    public async Task<ErrorOr<SupportMessagePage>> ListMessagesAsync(
        SupportCaller caller,
        Guid caseId,
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadAuthorizedAsync(caller, caseId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        var row = loaded.Value;
        var position = SupportCursors.ReadMessage(cursor, caseId);
        if (position.IsError)
            return position.Errors;
        var messages = await store.ListMessagesAsync(
            new(caseId, position.Value),
            cancellationToken
        );
        if (messages.IsError || messages.Value is null || messages.Value.Count > 21)
            return SupportErrors.Unavailable;
        var ids = new HashSet<Guid>();
        var last = position.Value;
        foreach (var message in messages.Value)
        {
            if (
                !ValidMessage(message, caseId)
                || !ids.Add(message.MessageId)
                || !SupportCursors.After(message, last)
            )
                return SupportErrors.Unavailable;
            last = new(message.WrittenAtRevision, message.MessageId);
        }
        var items = new List<SupportMessageView>();
        foreach (var message in messages.Value.Take(20))
        {
            if (row.IsExpired(timeProvider.GetUtcNow()))
                return SupportErrors.NotFound;
            var text = await protector.UnprotectAsync(
                message.ProtectedBody,
                new(row.OwnerId, caseId, message.MessageId, message.WrittenAtRevision),
                cancellationToken
            );
            if (row.IsExpired(timeProvider.GetUtcNow()))
                return SupportErrors.NotFound;
            if (text.IsError || SupportBody.Create(text.Value).IsError)
                return SupportErrors.Unavailable;
            items.Add(
                new(
                    caseId,
                    message.MessageId,
                    message.ActorKind,
                    text.Value,
                    message.WrittenAtRevision,
                    message.CreatedAt
                )
            );
        }
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        return new SupportMessagePage(
            caseId,
            items.AsReadOnly(),
            messages.Value.Count > 20
                ? SupportCursors.WriteMessage(
                    caseId,
                    new(messages.Value[19].WrittenAtRevision, messages.Value[19].MessageId)
                )
                : null
        );
    }

    private async Task<ErrorOr<SupportCase>> LoadAuthorizedAsync(
        SupportCaller caller,
        Guid caseId,
        CancellationToken cancellationToken
    )
    {
        var guard = Guard(caller);
        if (guard is { } denied)
            return denied;
        if (caseId == Guid.Empty)
            return SupportErrors.InvalidInput;
        var loaded = await store.LoadCaseAsync(caseId, cancellationToken);
        if (loaded.IsError)
            return SupportErrors.Unavailable;
        if (
            loaded.Value is not { } row
            || caller.Kind == SupportActorKind.Customer && row.OwnerId != caller.Subject
            || row.IsExpired(timeProvider.GetUtcNow())
        )
            return SupportErrors.NotFound;
        return row.CaseId != caseId || !ValidCase(row) ? SupportErrors.Unavailable : row;
    }

    private async Task<ErrorOr<SupportCaseView>> ReadCaseAsync(
        SupportCase row,
        CancellationToken cancellationToken
    )
    {
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        var text = await protector.UnprotectAsync(
            row.ProtectedTitle,
            new(row.OwnerId, row.CaseId, null, row.TitleWrittenAtRevision),
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (text.IsError || SupportTitle.Create(text.Value).IsError)
            return SupportErrors.Unavailable;
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        return new SupportCaseView(
            row.CaseId,
            row.OwnerId,
            text.Value,
            row.State,
            row.Revision,
            row.Category,
            row.Priority,
            row.TriageSource,
            row.OrderLink?.OrderId,
            row.OrderLink?.VerifiedAt,
            row.OrderLink?.StreamVersion,
            row.CreatedAt,
            row.UpdatedAt,
            row.ResolvedAt,
            row.PurgeAt
        );
    }

    private async Task<ErrorOr<SupportMutationResult>> ReplayCreateAsync(
        SupportCaller caller,
        CreateSupportCase command,
        SupportCase row,
        CancellationToken cancellationToken
    )
    {
        if (row.OwnerId != caller.Subject || row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (row.CaseId != command.CaseId || !ValidCase(row))
            return SupportErrors.Unavailable;
        if (row.FirstMessageId != command.MessageId || row.OrderLink?.OrderId != command.OrderId)
            return SupportErrors.Conflict;
        var retained = await store.LoadReceiptAsync(
            command.CaseId,
            command.MessageId,
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (retained.IsError)
            return SupportErrors.Unavailable;
        if (
            retained.Value is not { } receipt
            || !ReceiptMatches(
                receipt,
                caller,
                command.CaseId,
                command.MessageId,
                SupportActionKind.Create,
                0
            )
            || receipt.RequestedOrderId != command.OrderId
        )
            return SupportErrors.Conflict;
        var message = await store.LoadMessageAsync(
            command.CaseId,
            command.MessageId,
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (
            message.IsError
            || message.Value is not { } original
            || !ValidMessage(original, command.CaseId)
            || original.ActorId != caller.Subject
            || original.ActorKind != SupportActorKind.Customer
            || original.WrittenAtRevision != 1
        )
            return SupportErrors.Unavailable;
        var title = await protector.UnprotectAsync(
            row.ProtectedTitle,
            new(row.OwnerId, row.CaseId, null, row.TitleWrittenAtRevision),
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (title.IsError)
            return SupportErrors.Unavailable;
        var body = await protector.UnprotectAsync(
            original.ProtectedBody,
            new(row.OwnerId, row.CaseId, original.MessageId, original.WrittenAtRevision),
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (body.IsError)
            return SupportErrors.Unavailable;
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        return
            string.Equals(title.Value, command.Title, StringComparison.Ordinal)
            && string.Equals(body.Value, command.Body, StringComparison.Ordinal)
            ? new SupportMutationResult(View(receipt), true)
            : SupportErrors.Conflict;
    }

    private async Task<ErrorOr<SupportMutationResult>> ReplayReplyAsync(
        SupportCaller caller,
        ReplyToSupportCase command,
        SupportCase row,
        SupportReceipt receipt,
        CancellationToken cancellationToken
    )
    {
        if (
            !ReceiptMatches(
                receipt,
                caller,
                command.CaseId,
                command.MessageId,
                command.Kind,
                command.ExpectedRevision
            )
            || receipt.MessageId != command.MessageId
        )
            return SupportErrors.Conflict;
        var message = await store.LoadMessageAsync(
            command.CaseId,
            command.MessageId,
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (
            message.IsError
            || message.Value is not { } original
            || !ValidMessage(original, command.CaseId)
            || original.ActorId != caller.Subject
            || original.ActorKind != caller.Kind
            || original.WrittenAtRevision != receipt.AppliedRevision
        )
            return SupportErrors.Unavailable;
        var body = await protector.UnprotectAsync(
            original.ProtectedBody,
            new(row.OwnerId, row.CaseId, original.MessageId, original.WrittenAtRevision),
            cancellationToken
        );
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        if (body.IsError)
            return SupportErrors.Unavailable;
        if (row.IsExpired(timeProvider.GetUtcNow()))
            return SupportErrors.NotFound;
        return string.Equals(body.Value, command.Body, StringComparison.Ordinal)
            ? new SupportMutationResult(View(receipt), true)
            : SupportErrors.Conflict;
    }

    private static ErrorOr<SupportMutationResult> ReplayAction(
        SupportCaller caller,
        ActOnSupportCase command,
        SupportReceipt receipt
    ) =>
        ReceiptMatches(
            receipt,
            caller,
            command.CaseId,
            command.ActionId,
            command.Kind,
            command.ExpectedRevision
        )
        && receipt.MessageId is null
        && receipt.RequestedCategory == command.Category
        && receipt.RequestedPriority == command.Priority
            ? new SupportMutationResult(View(receipt), true)
            : SupportErrors.Conflict;

    private static SupportReceipt NewReceipt(
        SupportCaller caller,
        SupportCase row,
        Guid actionId,
        SupportActionKind kind,
        int expectedRevision,
        Guid? messageId,
        ManualCategory? previousCategory,
        SupportPriority? previousPriority,
        ManualCategory? requestedCategory,
        SupportPriority? requestedPriority
    ) =>
        new(
            row.CaseId,
            actionId,
            kind,
            caller.Subject,
            caller.Kind,
            expectedRevision,
            row.Revision,
            row.State,
            row.UpdatedAt,
            messageId,
            null,
            previousCategory,
            previousPriority,
            requestedCategory,
            requestedPriority
        );

    private static SupportActionReceipt View(SupportReceipt receipt) =>
        new(
            receipt.CaseId,
            receipt.ActionId,
            receipt.Kind,
            receipt.AppliedRevision,
            receipt.AppliedState,
            receipt.AppliedAt,
            receipt.MessageId
        );

    private static bool ReceiptMatches(
        SupportReceipt receipt,
        SupportCaller caller,
        Guid caseId,
        Guid actionId,
        SupportActionKind kind,
        int expectedRevision
    ) =>
        ValidReceipt(receipt)
        && receipt.CaseId == caseId
        && receipt.ActionId == actionId
        && receipt.Kind == kind
        && receipt.ExpectedRevision == expectedRevision
        && ReceiptActorMatches(receipt, caller);

    private static bool ReceiptActorMatches(SupportReceipt receipt, SupportCaller caller) =>
        receipt.ActorId == caller.Subject
        && receipt.ActorKind == caller.Kind
        && (
            caller.Kind == SupportActorKind.Customer
                ? receipt.Kind
                    is SupportActionKind.Create
                        or SupportActionKind.CustomerReply
                        or SupportActionKind.ReplyAndReopen
                : receipt.Kind
                    is SupportActionKind.OperatorReply
                        or SupportActionKind.Resolve
                        or SupportActionKind.SetTriage
        );

    private static bool ValidReceipt(SupportReceipt receipt) =>
        receipt.AppliedRevision > 0
        && Enum.IsDefined(receipt.Kind)
        && Enum.IsDefined(receipt.AppliedState)
        && (
            receipt.Kind
                is SupportActionKind.Create
                    or SupportActionKind.CustomerReply
                    or SupportActionKind.ReplyAndReopen
                    or SupportActionKind.OperatorReply
                ? receipt.MessageId == receipt.ActionId
                : receipt.MessageId is null
        );

    private static bool ValidMessage(SupportMessage message, Guid caseId) =>
        message.CaseId == caseId
        && message.MessageId != Guid.Empty
        && message.ActorId != Guid.Empty
        && Enum.IsDefined(message.ActorKind)
        && message.WrittenAtRevision > 0
        && !string.IsNullOrWhiteSpace(message.ProtectedBody);

    private static bool ValidCase(SupportCase row) =>
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

    private static Error? Guard(SupportCaller caller, SupportActorKind? required = null)
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

    // Match the precision of persisted timestamptz values, including the first receipt response.
    private DateTimeOffset WriteNow() =>
        new(timeProvider.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero);
}
