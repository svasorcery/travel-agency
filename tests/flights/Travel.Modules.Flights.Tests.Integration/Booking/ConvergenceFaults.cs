using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Wolverine;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

public enum ConvergenceFaultWindow
{
    BeforeProjectionSave,
    AfterProjectionCommit,
    BeforeWebhookAcknowledgement,
}

// All gates have bounded waits and are released by fixture cleanup, including failed assertions.
public sealed class ConvergenceFaults : SaveChangesInterceptor
{
    public Guid AggregateId { get; set; }
    public Guid InboxId { get; set; }
    public ConvergenceFaultWindow Window { get; set; }
    public bool Armed { get; set; }
    public TaskCompletionSource AtFault { get; } = Signal();
    public TaskCompletionSource ReleaseFault { get; } = Signal();
    public TaskCompletionSource AtRetry { get; } = Signal();
    public TaskCompletionSource ReleaseRetry { get; } = Signal();
    public ConcurrentQueue<Guid> ReconcileEnvelopes { get; } = new();
    public ConcurrentQueue<Guid> WebhookEnvelopes { get; } = new();
    public ConcurrentQueue<Guid> ProjectionContexts { get; } = new();
    public ConcurrentQueue<DbContextId> WebhookContexts { get; } = new();
    public ConcurrentQueue<(Guid Id, long? Version)> Notifications { get; } = new();
    private int _faults;
    public int InjectedFaults => _faults;

    public static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task WaitAsync(Task task, CancellationToken ct) =>
        task.WaitAsync(TimeSpan.FromSeconds(45), ct);

    public async Task BeforeDeliveryAsync(Guid envelopeId, bool webhook, CancellationToken ct)
    {
        var deliveries = webhook ? WebhookEnvelopes : ReconcileEnvelopes;
        deliveries.Enqueue(envelopeId);
        if (
            deliveries.Count > 1
            && webhook == (Window == ConvergenceFaultWindow.BeforeWebhookAcknowledgement)
        )
        {
            AtRetry.TrySetResult();
            await WaitAsync(ReleaseRetry.Task, ct);
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (!Armed)
            return result;
        var db = eventData.Context!;
        if (
            db
                .ChangeTracker.Entries<OrderReadModelEntity>()
                .Any(x => x.Entity.AggregateId == AggregateId)
        )
        {
            ProjectionContexts.Enqueue(db.ContextId.InstanceId);
            if (Window == ConvergenceFaultWindow.BeforeProjectionSave)
                await FailOnceAsync(cancellationToken);
        }
        if (
            db
                .ChangeTracker.Entries<WebhookInboxEntity>()
                .Any(x => x.Entity.Id == InboxId && x.Entity.ProcessedAt != null)
        )
        {
            WebhookContexts.Enqueue(db.ContextId);
            if (Window == ConvergenceFaultWindow.BeforeWebhookAcknowledgement)
                await FailOnceAsync(cancellationToken);
        }
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (
            Armed
            && Window == ConvergenceFaultWindow.AfterProjectionCommit
            && eventData
                .Context!.ChangeTracker.Entries<OrderReadModelEntity>()
                .Any(x => x.Entity.AggregateId == AggregateId)
        )
            await FailOnceAsync(cancellationToken);
        return result;
    }

    private async Task FailOnceAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _faults, 1, 0) != 0)
            return;
        AtFault.TrySetResult();
        await WaitAsync(ReleaseFault.Task, ct);
        throw new DbUpdateException(
            "Test-only crash window",
            new PostgresException("deadlock", "ERROR", "ERROR", "40P01")
        );
    }

    public void ReleaseAll()
    {
        ReleaseFault.TrySetResult();
        ReleaseRetry.TrySetResult();
    }
}

public sealed class ConvergenceReconcileMiddleware
{
    public static Task BeforeAsync(
        ReconcileOrderReadModel message,
        IMessageContext context,
        ConvergenceFaults faults,
        CancellationToken ct
    ) =>
        faults.Armed && message.AggregateId == faults.AggregateId
            ? faults.BeforeDeliveryAsync(context.Envelope!.Id, false, ct)
            : Task.CompletedTask;
}

public sealed class ConvergenceWebhookMiddleware
{
    public static Task BeforeAsync(
        ProcessDuffelWebhookCommand message,
        IMessageContext context,
        ConvergenceFaults faults,
        CancellationToken ct
    ) =>
        faults.Armed && message.InboxId == faults.InboxId
            ? faults.BeforeDeliveryAsync(context.Envelope!.Id, true, ct)
            : Task.CompletedTask;
}

public sealed class ConvergenceNotificationMiddleware
{
    public static async Task BeforeAsync(
        IMessageContext context,
        ConvergenceFaults faults,
        CancellationToken ct
    )
    {
        var (id, version) = context.Envelope!.Message switch
        {
            OrderConfirmedNotification n => (n.AggregateId, n.RequiredStreamVersion),
            OrderTicketedNotification n => (n.AggregateId, n.RequiredStreamVersion),
            _ => (Guid.Empty, (long?)null),
        };
        if (!faults.Armed || id != faults.AggregateId)
            return;
        var envelopeId = context.Envelope.Id;
        var retry = faults.Notifications.Any(x => x.Id == envelopeId);
        faults.Notifications.Enqueue((envelopeId, version));
        if (retry)
            await ConvergenceFaults.WaitAsync(faults.ReleaseRetry.Task, ct);
    }
}
