using System.Text.Json;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Webhooks;

public static class DuffelWebhookHandler
{
    // The explicit booking helper owns the commit and conflict translation.
    // Do not let generated middleware attempt a second save after a rejected write.
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        ProcessDuffelWebhookCommand cmd,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IFlightsMetrics metrics,
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger<ProcessDuffelWebhookCommand> log,
        CancellationToken ct
    )
    {
        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["inbox_id"] = cmd.InboxId,
                ["correlation_id"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            }
        );

        // 1. Load inbox entry — idempotency guard
        var entry = await inbox.FindAsync(cmd.InboxId, ct);
        if (entry is null)
        {
            log.LogWarning("WebhookInbox row {InboxId} not found — skipping.", cmd.InboxId);
            return;
        }

        if (entry.ProcessedAt is not null)
        {
            log.LogDebug(
                "WebhookInbox row {InboxId} already processed at {ProcessedAt} — skipping.",
                cmd.InboxId,
                entry.ProcessedAt
            );
            return;
        }

        // 2. Parse raw payload defensively
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(entry.RawPayload);
        }
        catch (JsonException ex)
        {
            log.LogWarning(
                ex,
                "WebhookInbox {InboxId}: failed to parse RawPayload as JSON — marking processed.",
                cmd.InboxId
            );
            await inbox.MarkProcessedAsync(cmd.InboxId, time.GetUtcNow(), ct);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("object", out var objectEl))
            {
                log.LogWarning(
                    "WebhookInbox {InboxId}: payload missing 'object' property — marking processed.",
                    cmd.InboxId
                );
                await inbox.MarkProcessedAsync(cmd.InboxId, time.GetUtcNow(), ct);
                return;
            }

            switch (entry.EventType)
            {
                case "order.created":
                    await HandleOrderCreated(
                        objectEl,
                        cmd.InboxId,
                        inbox,
                        marten,
                        outbox,
                        time,
                        log,
                        ct
                    );
                    break;

                case "order.airline_initiated_change.cancelled":
                    await HandleAirlineInitiatedCancellation(
                        objectEl,
                        cmd.InboxId,
                        inbox,
                        marten,
                        outbox,
                        time,
                        log,
                        ct
                    );
                    break;

                case "order.airline_initiated_change":
                    // Non-cancelled airline-initiated change (schedule change, equipment
                    // swap, etc.). No domain event is appended in M1 — we record a
                    // counter for ops visibility and log at Information so the inbox
                    // row is still marked processed by the caller.
                    log.LogInformation(
                        "WebhookInbox {InboxId}: order.airline_initiated_change — recording metric, no domain action.",
                        cmd.InboxId
                    );
                    metrics.RecordAirlineInitiatedChange();
                    break;

                default:
                    log.LogInformation(
                        "WebhookInbox {InboxId}: unhandled event type '{EventType}' — no domain action.",
                        cmd.InboxId,
                        entry.EventType
                    );
                    break;
            }
        }

        // 3. Mark processed — record processing lag from when the webhook was received
        var processedAt = time.GetUtcNow();
        var lagMs = (processedAt - entry.ReceivedAt).TotalMilliseconds;
        metrics.RecordWebhookProcessingLag(lagMs, entry.EventType);
        await inbox.MarkProcessedAsync(cmd.InboxId, processedAt, ct);
    }

    private static async Task HandleOrderCreated(
        JsonElement objectEl,
        Guid inboxId,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger log,
        CancellationToken ct
    )
    {
        if (!objectEl.TryGetProperty("id", out var idEl))
        {
            log.LogWarning(
                "WebhookInbox {InboxId}: order.created object missing 'id' — skipping domain action.",
                inboxId
            );
            return;
        }

        var duffelOrderId = idEl.GetString();
        if (string.IsNullOrEmpty(duffelOrderId))
            return;

        // Extract ticket documents
        if (
            !objectEl.TryGetProperty("documents", out var docsEl)
            || docsEl.ValueKind != JsonValueKind.Array
        )
            return;

        var ticketNumbers = new List<string>();
        foreach (var docEl in docsEl.EnumerateArray())
        {
            if (
                docEl.TryGetProperty("type", out var typeEl)
                && typeEl.GetString() == "ticket"
                && docEl.TryGetProperty("unique_identifier", out var uidEl)
            )
            {
                var uid = uidEl.GetString();
                if (!string.IsNullOrEmpty(uid))
                    ticketNumbers.Add(uid);
            }
        }

        if (ticketNumbers.Count == 0)
        {
            log.LogDebug(
                "WebhookInbox {InboxId}: order.created has no ticket documents — skipping.",
                inboxId
            );
            return;
        }

        var aggregateId = await inbox.FindAggregateIdByProviderOrderIdAsync(duffelOrderId, ct);
        if (aggregateId == default)
            throw BookingCorrelationNotReadyException.ForProviderOrder(duffelOrderId);

        using var _orderId = log.BeginScope(
            new Dictionary<string, object> { ["order_id"] = aggregateId }
        );

        var stream = await marten.Events.FetchForWriting<BookingAggregate>(aggregateId, ct);
        var existing = stream.Aggregate;
        if (existing is null)
            throw new BookingTransitionRejectedException(
                new BookingRejection(
                    BookingTransition.Ticket,
                    BookingRejectionCode.InvalidState,
                    BookingStatus.None
                )
            );

        var decision = existing.DecideTicket();
        if (decision is BookingTransitionDecision.IdempotentNoOp)
            return;
        if (decision is BookingTransitionDecision.Rejected rejected)
            BookingTransitionErrorMapper.ThrowForDurableMessage(rejected.Reason);
        if (existing.OwnerUserId is not { } ownerUserId)
            throw new BookingSourceOwnershipMissingException(aggregateId);

        var ticketed = new OrderTicketed(
            new EquatableArray<string>([.. ticketNumbers]),
            time.GetUtcNow()
        );
        var requiredVersion = stream.CurrentVersion + 1;
        stream.AppendOne(ticketed);
        await marten.SaveBookingWithReconcileAsync(
            outbox,
            aggregateId,
            [new OrderTicketedNotification(aggregateId, ownerUserId, requiredVersion)],
            ct
        );
    }

    private static async Task HandleAirlineInitiatedCancellation(
        JsonElement objectEl,
        Guid inboxId,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger log,
        CancellationToken ct
    )
    {
        if (!objectEl.TryGetProperty("id", out var idEl))
        {
            log.LogWarning(
                "WebhookInbox {InboxId}: airline_initiated_change object missing 'id' — skipping.",
                inboxId
            );
            return;
        }

        var duffelOrderId = idEl.GetString();
        if (string.IsNullOrEmpty(duffelOrderId))
            return;

        var aggregateId = await inbox.FindAggregateIdByProviderOrderIdAsync(duffelOrderId, ct);
        if (aggregateId == default)
            throw BookingCorrelationNotReadyException.ForProviderOrder(duffelOrderId);

        using var _orderId = log.BeginScope(
            new Dictionary<string, object> { ["order_id"] = aggregateId }
        );

        var stream = await marten.Events.FetchForWriting<BookingAggregate>(aggregateId, ct);
        var agg = stream.Aggregate;
        if (agg is null)
            throw new BookingTransitionRejectedException(
                new BookingRejection(
                    BookingTransition.Refund,
                    BookingRejectionCode.InvalidState,
                    BookingStatus.None
                )
            );

        var decision = agg.DecideRefund();
        if (decision is BookingTransitionDecision.IdempotentNoOp)
            return;
        if (decision is BookingTransitionDecision.Rejected rejected)
            BookingTransitionErrorMapper.ThrowForDurableMessage(rejected.Reason);
        if (agg.OwnerUserId is not { })
            throw new BookingSourceOwnershipMissingException(aggregateId);

        if (agg.TotalAmount is not { } refundAmount)
            throw new BookingTransitionRejectedException(
                new BookingRejection(
                    BookingTransition.Refund,
                    BookingRejectionCode.InvalidState,
                    agg.Status
                )
            );

        var refunded = new OrderRefunded(
            new RefundRef(Guid.NewGuid()),
            refundAmount,
            RefundInitiator.Airline,
            time.GetUtcNow()
        );
        stream.AppendOne(refunded);
        await marten.SaveBookingWithReconcileAsync(outbox, aggregateId, [], ct);
    }
}
