using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ErrorOr;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Application;

public sealed class SupportClassificationService(
    ISupportCaseStore store,
    ISupportTextProtector protector,
    ISupportClassificationProvider provider,
    IFlightsSupportTriageContext orders,
    SupportClassificationAdmission admission,
    TimeProvider time
) : ISupportClassificationService
{
    private static readonly JsonSerializerOptions StateJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ErrorOr<SupportClassificationResult>> SuggestAsync(
        SupportCaller caller,
        RequestSupportClassification request,
        CancellationToken ct = default
    )
    {
        var started = time.GetTimestamp();
        try
        {
            var result = await SuggestCoreAsync(caller, request, ct);
            var elapsed = time.GetElapsedTime(started).TotalMilliseconds;
            if (result.IsError)
                SupportClassificationTelemetry.RecordError(elapsed);
            else
                SupportClassificationTelemetry.Record(result.Value, elapsed);
            return result;
        }
        catch (OperationCanceledException)
        {
            SupportClassificationTelemetry.RecordError(
                time.GetElapsedTime(started).TotalMilliseconds
            );
            throw;
        }
    }

    private async Task<ErrorOr<SupportClassificationResult>> SuggestCoreAsync(
        SupportCaller caller,
        RequestSupportClassification request,
        CancellationToken ct = default
    )
    {
        if (SupportCaseAccess.Guard(caller, SupportActorKind.Operator) is { } denied)
            return denied;
        var id = request.RequestId.ToString("N");
        if (
            request.CaseId == Guid.Empty
            || request.ExpectedRevision <= 0
            || id[12] != '4'
            || !"89ab".Contains(id[16])
        )
            return SupportErrors.InvalidInput;
        ct.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        SupportCase? captured = null;
        try
        {
            var loaded = await CurrentAsync(caller, request, lifetime.Token);
            if (loaded.IsError)
                return loaded.Errors;
            captured = loaded.Value;
            if (!provider.IsEnabled)
                return Result(request, SupportClassificationReason.Disabled);
            using var lease = admission.TryAcquire(
                caller.Subject,
                request.CaseId,
                request.ExpectedRevision
            );
            if (lease is null)
                return SupportErrors.ClassificationBusy;
            return await ClassifyAsync(caller, request, captured, lifetime.Token)
                .WaitAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return captured is null
                ? SupportErrors.Unavailable
                : Result(request, SupportClassificationReason.Uncertain);
        }
    }

    private async Task<ErrorOr<SupportClassificationResult>> ClassifyAsync(
        SupportCaller caller,
        RequestSupportClassification request,
        SupportCase row,
        CancellationToken ct
    )
    {
        var messages = await store.ListMessagesAsync(new(row.CaseId, null, 5), ct).WaitAsync(ct);
        if (
            messages.IsError
            || messages.Value is null
            || messages.Value.Count == 0
            || messages.Value.Count > 5
        )
            return SupportErrors.Unavailable;
        if (messages.Value.Count > 4)
            return Result(request, SupportClassificationReason.InputTooLarge);
        if (
            messages.Value[0].MessageId != row.FirstMessageId
            || messages.Value[0].WrittenAtRevision != 1
        )
            return SupportErrors.Unavailable;
        var ids = new HashSet<Guid>();
        var previous = 0;
        foreach (var message in messages.Value)
        {
            if (
                !SupportCaseAccess.ValidMessage(message, row.CaseId)
                || !ids.Add(message.MessageId)
                || message.WrittenAtRevision <= previous
                || message.WrittenAtRevision > row.Revision
            )
                return SupportErrors.Unavailable;
            previous = message.WrittenAtRevision;
        }
        if (row.IsExpired(time.GetUtcNow()))
            return SupportErrors.NotFound;
        var title = await protector
            .UnprotectAsync(
                row.ProtectedTitle,
                new(row.OwnerId, row.CaseId, null, row.TitleWrittenAtRevision),
                ct
            )
            .WaitAsync(ct);
        if (row.IsExpired(time.GetUtcNow()))
            return SupportErrors.NotFound;
        if (title.IsError || SupportTitle.Create(title.Value).IsError)
            return SupportErrors.Unavailable;
        var text = new List<object>();
        foreach (var message in messages.Value)
        {
            ct.ThrowIfCancellationRequested();
            if (row.IsExpired(time.GetUtcNow()))
                return SupportErrors.NotFound;
            var body = await protector
                .UnprotectAsync(
                    message.ProtectedBody,
                    new(row.OwnerId, row.CaseId, message.MessageId, message.WrittenAtRevision),
                    ct
                )
                .WaitAsync(ct);
            if (row.IsExpired(time.GetUtcNow()))
                return SupportErrors.NotFound;
            if (body.IsError || SupportBody.Create(body.Value).IsError)
                return SupportErrors.Unavailable;
            text.Add(new { role = message.ActorKind.ToString(), body = body.Value });
        }
        var state = JsonSerializer.Serialize(
            new { title = title.Value, messages = text },
            StateJson
        );
        var scalars = 0;
        foreach (var _ in state.EnumerateRunes())
            if (++scalars > 8000)
                return Result(request, SupportClassificationReason.InputTooLarge);
        if (Encoding.UTF8.GetByteCount(state) > 24 * 1024)
            return Result(request, SupportClassificationReason.InputTooLarge);
        var before = await CurrentAsync(caller, request, ct);
        if (before.IsError)
            return before.Errors;
        ct.ThrowIfCancellationRequested();
        var outcome = await provider.ClassifyAsync(new(state), ct).WaitAsync(ct);
        ct.ThrowIfCancellationRequested();
        var classification = outcome.IsError
            ? new SupportProviderClassification(null, SupportClassificationReason.Unavailable)
            : outcome.Value;
        var reason =
            classification is null || !Enum.IsDefined(classification.Reason)
                ? SupportClassificationReason.InvalidResponse
                : classification.Reason;
        var candidate =
            classification is not null
            && reason == SupportClassificationReason.Ready
            && classification.Category is { } category
            && SupportClassificationPolicy.IsCandidate(category)
            && classification.ServingProfileId is { Length: 64 } profile
            && profile.All(char.IsAsciiHexDigit);
        if (!candidate && reason == SupportClassificationReason.Ready)
            reason = SupportClassificationReason.InvalidResponse;
        // Disabled is reserved for the initial path that does no private processing.
        if (reason == SupportClassificationReason.Disabled)
            reason = SupportClassificationReason.IdentityMismatch;
        FlightSupportTriageFacts? trustedFacts = null;
        if (row.OrderLink is { } link)
        {
            var facts = await orders.ReadAsync(row.OwnerId, link.OrderId, ct).WaitAsync(ct);
            if (
                !facts.IsError
                && facts.Value is { } value
                && value.OwnerId == row.OwnerId
                && value.OrderId == link.OrderId
                && value.StreamVersion >= link.StreamVersion
                && Enum.IsDefined(value.Journey)
            )
                trustedFacts = value;
        }
        var after = await CurrentAsync(caller, request, ct);
        if (after.IsError)
            return after.Errors;
        ct.ThrowIfCancellationRequested();
        if (candidate && !provider.IsEnabled)
        {
            candidate = false;
            reason = SupportClassificationReason.IdentityMismatch;
        }
        SupportClassificationTelemetry.RecordInput(
            Encoding.UTF8.GetByteCount(state),
            classification?.InputTokens
        );
        return Result(
            request,
            reason,
            candidate ? classification : null,
            row.Priority,
            trustedFacts
        );
    }

    private async Task<ErrorOr<SupportCase>> CurrentAsync(
        SupportCaller caller,
        RequestSupportClassification request,
        CancellationToken ct
    )
    {
        var loaded = await SupportCaseAccess.LoadAsync(store, time, caller, request.CaseId, ct);
        if (loaded.IsError)
            return loaded.Errors;
        return
            loaded.Value.Revision != request.ExpectedRevision
            || loaded.Value.State == CaseState.Resolved
            ? SupportErrors.Conflict
            : loaded.Value;
    }

    private SupportClassificationResult Result(
        RequestSupportClassification request,
        SupportClassificationReason reason,
        SupportProviderClassification? candidate = null,
        SupportPriority priority = SupportPriority.Normal,
        FlightSupportTriageFacts? facts = null
    )
    {
        var now = time.GetUtcNow();
        // The rule and public timestamps share the same final clock observation.
        var journey = facts?.Journey switch
        {
            FlightSupportJourneyState.Active => SupportJourneyState.Active,
            FlightSupportJourneyState.Closed => SupportJourneyState.Closed,
            _ => SupportJourneyState.Unknown,
        };
        var urgency = SupportClassificationPolicy.RecommendUrgency(
            priority,
            journey,
            facts?.NextScheduledDepartureAt,
            facts?.ObservedAt,
            facts?.StreamVersion,
            now
        );
        return new(
            request.CaseId,
            request.RequestId,
            request.ExpectedRevision,
            now,
            now.AddMinutes(2),
            candidate is not null ? SupportClassificationStatus.Candidate
                : reason == SupportClassificationReason.Disabled
                    ? SupportClassificationStatus.Disabled
                : SupportClassificationStatus.ManualRequired,
            reason,
            candidate?.Category,
            candidate?.ServingProfileId,
            candidate is not null ? SupportClassificationInput.QuestionVersion : null,
            urgency
        );
    }
}
