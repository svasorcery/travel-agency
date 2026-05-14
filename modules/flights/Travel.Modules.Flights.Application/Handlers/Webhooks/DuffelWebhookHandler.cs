using System.Text.Json;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Webhooks;

public static class DuffelWebhookHandler
{
    [WolverineHandler]
    public static async Task Handle(
        ProcessDuffelWebhookCommand cmd,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IOrderReadModelProjector projector,
        TimeProvider time,
        ILogger<ProcessDuffelWebhookCommand> log,
        CancellationToken ct
    )
    {
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
                        projector,
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
                        projector,
                        time,
                        log,
                        ct
                    );
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

        // 3. Mark processed
        await inbox.MarkProcessedAsync(cmd.InboxId, time.GetUtcNow(), ct);
    }

    private static async Task HandleOrderCreated(
        JsonElement objectEl,
        Guid inboxId,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IOrderReadModelProjector projector,
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
        {
            log.LogWarning(
                "WebhookInbox {InboxId}: no aggregate found for Duffel order '{DuffelOrderId}'.",
                inboxId,
                duffelOrderId
            );
            return;
        }

        marten.Events.Append(aggregateId, new OrderTicketed(ticketNumbers, time.GetUtcNow()));
        await marten.SaveChangesAsync(ct);

        var agg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            aggregateId,
            token: ct
        );
        if (agg is not null)
        {
            var userId = await inbox.FindUserIdByAggregateIdAsync(aggregateId, ct) ?? Guid.Empty;
            await projector.Project(agg, userId, time, ct);
        }
    }

    private static async Task HandleAirlineInitiatedCancellation(
        JsonElement objectEl,
        Guid inboxId,
        IWebhookInboxStore inbox,
        IDocumentSession marten,
        IOrderReadModelProjector projector,
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
        {
            log.LogWarning(
                "WebhookInbox {InboxId}: no aggregate found for Duffel order '{DuffelOrderId}'.",
                inboxId,
                duffelOrderId
            );
            return;
        }

        var agg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            aggregateId,
            token: ct
        );
        if (agg is null)
        {
            log.LogWarning(
                "WebhookInbox {InboxId}: Marten stream not found for aggregate {AggregateId}.",
                inboxId,
                aggregateId
            );
            return;
        }

        var refundAmount =
            agg.TotalAmount ?? Money.Create(0m, CurrencyCode.Create("USD").Value).Value;

        marten.Events.Append(
            aggregateId,
            new OrderRefunded(
                new RefundRef(Guid.NewGuid()),
                refundAmount,
                RefundInitiator.Airline,
                time.GetUtcNow()
            )
        );
        await marten.SaveChangesAsync(ct);

        var updatedAgg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            aggregateId,
            token: ct
        );
        if (updatedAgg is not null)
        {
            var userId = await inbox.FindUserIdByAggregateIdAsync(aggregateId, ct) ?? Guid.Empty;
            await projector.Project(updatedAgg, userId, time, ct);
        }
    }
}
