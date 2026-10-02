using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.ReadModels;
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
        IWebhookPayloadReader reader,
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

        var parsed = reader.Read(entry);
        if (parsed.IsError)
        {
            if (parsed.FirstError.Code == "Flights.PiiEnvelopeInvalid")
                throw new BookingProjectionTerminalException("PiiEnvelopeInvalid");
            throw new BookingProjectionTransientException("PiiPayloadUnavailable");
        }
        var facts = parsed.Value;
        switch (facts.Kind)
        {
            case BookingWebhookKind.TicketsAvailable:
                await HandleOrderCreated(facts, inbox, marten, outbox, time, ct);
                break;
            case BookingWebhookKind.AirlineCancelled:
                await HandleAirlineInitiatedCancellation(facts, inbox, marten, outbox, time, ct);
                break;
            case BookingWebhookKind.AirlineChanged:
                metrics.RecordAirlineInitiatedChange();
                break;
            default:
                log.LogInformation(
                    "WebhookInbox {InboxId}: no actionable booking facts.",
                    cmd.InboxId
                );
                break;
        }

        // 3. Mark processed — record processing lag from when the webhook was received
        var processedAt = time.GetUtcNow();
        var lagMs = (processedAt - entry.ReceivedAt).TotalMilliseconds;
        metrics.RecordWebhookProcessingLag(lagMs, entry.EventType);
        await inbox.MarkProcessedAsync(cmd.InboxId, processedAt, ct);
    }

    private static async Task HandleOrderCreated(
        BookingWebhookFacts facts,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var duffelOrderId = facts.ProviderOrderId!;
        var ticketNumbers = facts.TicketNumbers;
        var aggregateId = await inbox.FindAggregateIdByProviderOrderIdAsync(duffelOrderId, ct);
        if (aggregateId == default)
            throw BookingCorrelationNotReadyException.ForProviderOrder(duffelOrderId);

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

        var ticketed = new OrderTicketed(ticketNumbers, time.GetUtcNow());
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
        BookingWebhookFacts facts,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var duffelOrderId = facts.ProviderOrderId!;
        var aggregateId = await inbox.FindAggregateIdByProviderOrderIdAsync(duffelOrderId, ct);
        if (aggregateId == default)
            throw BookingCorrelationNotReadyException.ForProviderOrder(duffelOrderId);

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
