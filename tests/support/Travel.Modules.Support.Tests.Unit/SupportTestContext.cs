using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Tests.Unit;

internal sealed class SupportTestContext
{
    public Guid CaseId { get; } = Guid.NewGuid();
    public Guid FirstMessageId { get; } = Guid.NewGuid();
    public Guid OrderId { get; } = Guid.NewGuid();
    public SupportCaller Customer { get; } = new(Guid.NewGuid(), SupportActorKind.Customer, true);
    public SupportCaller Operator { get; } =
        new(Guid.NewGuid(), SupportActorKind.Operator, true, true, "travel-support-operator");
    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
    public MemorySupportStore Store { get; } = new();
    public FakeSupportProtector Protector { get; } = new();
    public FakeOrderAccess Orders { get; }
    public SupportCaseService Service { get; }
    public CreateSupportCase CreateCommand =>
        new(CaseId, FirstMessageId, " A literal <title> ", " A literal first message ");

    public SupportTestContext()
    {
        Orders = new FakeOrderAccess(Clock);
        Service = new(Store, Protector, Orders, Clock);
    }

    public Task<ErrorOr<SupportMutationResult>> CreateAsync() =>
        Service.CreateAsync(Customer, CreateCommand, TestContext.Current.CancellationToken);

    public Task<ErrorOr<SupportMutationResult>> ReplyAsync(
        SupportCaller caller,
        int revision,
        SupportActionKind kind
    ) =>
        Service.ReplyAsync(
            caller,
            new(CaseId, Guid.NewGuid(), "reply text", revision, kind),
            TestContext.Current.CancellationToken
        );
}

internal sealed class FakeSupportProtector : ISupportTextProtector
{
    private readonly object gate = new();
    private readonly Dictionary<string, (string Text, SupportTextBinding Binding)> values = [];
    public bool Unavailable { get; set; }
    public int UnprotectCalls { get; set; }
    public int ProtectCalls { get; private set; }
    public Action? BeforeUnprotect { get; set; }

    public Task<ErrorOr<string>> ProtectAsync(
        string plaintext,
        SupportTextBinding binding,
        CancellationToken cancellationToken
    )
    {
        ProtectCalls++;
        if (Unavailable)
            return Task.FromResult<ErrorOr<string>>(SupportErrors.Unavailable);
        var envelope = Guid.NewGuid().ToString("N");
        lock (gate)
            values.Add(envelope, (plaintext, binding));
        return Task.FromResult<ErrorOr<string>>(envelope);
    }

    public Task<ErrorOr<string>> UnprotectAsync(
        string envelope,
        SupportTextBinding binding,
        CancellationToken cancellationToken
    )
    {
        UnprotectCalls++;
        BeforeUnprotect?.Invoke();
        lock (gate)
            return Task.FromResult<ErrorOr<string>>(
                Unavailable
                || !values.TryGetValue(envelope, out var value)
                || value.Binding != binding
                    ? SupportErrors.Unavailable
                    : value.Text
            );
    }
}

internal sealed class FakeOrderAccess(TimeProvider clock) : IFlightsSupportOrderAccess
{
    public int Calls { get; private set; }
    public bool Unavailable { get; set; }
    public bool Missing { get; set; }
    public bool WrongOwner { get; set; }

    public Task<ErrorOr<FlightOrderOwnershipProof>> VerifyOwnershipAsync(
        Guid ownerId,
        Guid orderId,
        CancellationToken cancellationToken = default
    )
    {
        Calls++;
        ErrorOr<FlightOrderOwnershipProof> result =
            Unavailable ? SupportErrors.Unavailable
            : Missing ? SupportErrors.NotFound
            : new FlightOrderOwnershipProof(
                orderId,
                WrongOwner ? Guid.NewGuid() : ownerId,
                7,
                clock.GetUtcNow()
            );
        return Task.FromResult(result);
    }
}

internal sealed class MemorySupportStore : ISupportCaseStore
{
    private readonly object gate = new();
    private TaskCompletionSource<bool>? readBarrier;
    private int barrierReaders;
    public Dictionary<Guid, SupportCase> Cases { get; } = [];
    public Dictionary<(Guid CaseId, Guid MessageId), SupportMessage> Messages { get; } = [];
    public Dictionary<(Guid CaseId, Guid ActionId), SupportReceipt> Receipts { get; } = [];
    public bool FailWrites { get; set; }
    public bool FailReads { get; set; }
    public bool NormalizeTimestamps { get; set; }
    public int WriteAttempts { get; private set; }
    public Action? AfterReceiptRead { get; set; }
    public Action? AfterCaseRead { get; set; }

    public void SynchronizeNextTwoCaseReads()
    {
        barrierReaders = 0;
        readBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task<ErrorOr<SupportCase?>> LoadCaseAsync(
        Guid caseId,
        CancellationToken cancellationToken
    )
    {
        var snapshot = Cases.GetValueOrDefault(caseId);
        var barrier = readBarrier;
        if (barrier is not null)
        {
            if (Interlocked.Increment(ref barrierReaders) == 2)
            {
                readBarrier = null;
                barrier.SetResult(true);
            }
            await barrier.Task.WaitAsync(cancellationToken);
        }
        AfterCaseRead?.Invoke();
        return await Read(snapshot);
    }

    public Task<ErrorOr<SupportReceipt?>> LoadReceiptAsync(
        Guid caseId,
        Guid actionId,
        CancellationToken cancellationToken
    )
    {
        var result = Read(Receipts.GetValueOrDefault((caseId, actionId)));
        AfterReceiptRead?.Invoke();
        return result;
    }

    public Task<ErrorOr<SupportMessage?>> LoadMessageAsync(
        Guid caseId,
        Guid messageId,
        CancellationToken cancellationToken
    ) => Read(Messages.GetValueOrDefault((caseId, messageId)));

    private Task<ErrorOr<T?>> Read<T>(T? value)
        where T : class =>
        Task.FromResult(FailReads ? (ErrorOr<T?>)SupportErrors.Unavailable : value);

    public Task<ErrorOr<IReadOnlyList<SupportCase>>> ListCasesAsync(
        SupportCaseQuery query,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<SupportCase> rows = Cases
            .Values.Where(row =>
                (!query.OwnerId.HasValue || row.OwnerId == query.OwnerId)
                && !row.IsExpired(query.Now)
                && (!query.Filters.State.HasValue || row.State == query.Filters.State)
                && (!query.Filters.Category.HasValue || row.Category == query.Filters.Category)
                && (!query.Filters.Priority.HasValue || row.Priority == query.Filters.Priority)
                && (
                    query.After is null
                    || row.CreatedAt > query.After.CreatedAt
                    || row.CreatedAt == query.After.CreatedAt
                        && row.CaseId.CompareTo(query.After.CaseId) > 0
                )
            )
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.CaseId)
            .Take(query.Limit)
            .ToArray();
        if (FailReads)
            return Task.FromResult<ErrorOr<IReadOnlyList<SupportCase>>>(SupportErrors.Unavailable);
        return Task.FromResult<ErrorOr<IReadOnlyList<SupportCase>>>(rows.ToArray());
    }

    public Task<ErrorOr<IReadOnlyList<SupportMessage>>> ListMessagesAsync(
        SupportMessageQuery query,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<SupportMessage> rows = Messages
            .Values.Where(row =>
                row.CaseId == query.CaseId
                && (
                    query.After is null
                    || row.WrittenAtRevision > query.After.WrittenAtRevision
                    || row.WrittenAtRevision == query.After.WrittenAtRevision
                        && row.MessageId.CompareTo(query.After.MessageId) > 0
                )
            )
            .OrderBy(row => row.WrittenAtRevision)
            .ThenBy(row => row.MessageId)
            .Take(query.Limit)
            .ToArray();
        if (FailReads)
            return Task.FromResult<ErrorOr<IReadOnlyList<SupportMessage>>>(
                SupportErrors.Unavailable
            );
        return Task.FromResult<ErrorOr<IReadOnlyList<SupportMessage>>>(rows.ToArray());
    }

    public Task<ErrorOr<Success>> TryCreateAsync(
        SupportCase supportCase,
        SupportMessage message,
        SupportReceipt receipt,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
        {
            WriteAttempts++;
            if (FailWrites)
                return Task.FromResult<ErrorOr<Success>>(SupportErrors.Unavailable);
            if (Cases.ContainsKey(supportCase.CaseId))
                return Task.FromResult<ErrorOr<Success>>(SupportErrors.Conflict);
            Cases.Add(supportCase.CaseId, Normalize(supportCase));
            Messages.Add((message.CaseId, message.MessageId), Normalize(message));
            Receipts.Add((receipt.CaseId, receipt.ActionId), Normalize(receipt));
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    public Task<ErrorOr<Success>> TryUpdateAsync(
        int expectedRevision,
        SupportCase supportCase,
        SupportMessage? message,
        SupportReceipt receipt,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
        {
            WriteAttempts++;
            if (FailWrites)
                return Task.FromResult<ErrorOr<Success>>(SupportErrors.Unavailable);
            if (
                !Cases.TryGetValue(supportCase.CaseId, out var previous)
                || previous.Revision != expectedRevision
                || Receipts.ContainsKey((receipt.CaseId, receipt.ActionId))
            )
                return Task.FromResult<ErrorOr<Success>>(SupportErrors.Conflict);
            Cases[supportCase.CaseId] = Normalize(supportCase);
            if (message is not null)
                Messages.Add((message.CaseId, message.MessageId), Normalize(message));
            Receipts.Add((receipt.CaseId, receipt.ActionId), Normalize(receipt));
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    private DateTimeOffset Microseconds(DateTimeOffset value) =>
        NormalizeTimestamps ? new(value.UtcTicks / 10 * 10, TimeSpan.Zero) : value;

    private SupportCase Normalize(SupportCase row) =>
        row with
        {
            CreatedAt = Microseconds(row.CreatedAt),
            UpdatedAt = Microseconds(row.UpdatedAt),
            ResolvedAt = row.ResolvedAt is { } resolved ? Microseconds(resolved) : null,
            PurgeAt = row.PurgeAt is { } purge ? Microseconds(purge) : null,
        };

    private SupportMessage Normalize(SupportMessage row) =>
        row with
        {
            CreatedAt = Microseconds(row.CreatedAt),
        };

    private SupportReceipt Normalize(SupportReceipt row) =>
        row with
        {
            AppliedAt = Microseconds(row.AppliedAt),
        };
}
