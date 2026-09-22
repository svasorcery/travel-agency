using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Runtime;

namespace Travel.Modules.Flights.Infrastructure.Diagnostics;

public sealed class BookingConsistencyDiagnostics(
    IWolverineRuntime runtime,
    IOrderReadModelReconciler reconciler,
    FlightsDbContext db
) : IBookingConsistencyDiagnostics
{
    private static readonly HashSet<string> AllowedTypes =
    [
        typeof(ReconcileOrderReadModel).FullName!,
        typeof(ProcessDuffelWebhookCommand).FullName!,
        typeof(OrderConfirmedNotification).FullName!,
        typeof(OrderCancelledNotification).FullName!,
        typeof(OrderTicketedNotification).FullName!,
    ];

    public async Task<BookingConsistencyInspection> InspectAsync(
        Guid aggregateId,
        CancellationToken ct
    )
    {
        var validation = await reconciler.ValidateAsync(aggregateId, ct);
        var records = new List<BookingEnvelopeDiagnostic>();
        foreach (var envelope in await runtime.Storage.Admin.AllIncomingAsync())
            if (await AggregateIdAsync(envelope, ct) == aggregateId)
                records.Add(
                    new(envelope.Id, envelope.MessageType!, envelope.Status.ToString(), false)
                );
        foreach (var envelope in await runtime.Storage.Admin.AllOutgoingAsync())
            if (await AggregateIdAsync(envelope, ct) == aggregateId)
                records.Add(new(envelope.Id, envelope.MessageType!, "Outgoing", false));
        var page = 1;
        while (true)
        {
            var results = await runtime.Storage.DeadLetters.QueryAsync(
                new DeadLetterEnvelopeQuery { PageNumber = page, PageSize = 100 },
                ct
            );
            foreach (var letter in results.Envelopes)
                if (await AggregateIdAsync(letter.Envelope, ct) == aggregateId)
                    records.Add(
                        new(letter.Id, letter.MessageType, "DeadLetter", letter.Replayable)
                    );
            if (page * 100 >= results.TotalCount)
                break;
            page++;
        }
        return new(validation, records);
    }

    public async Task<BookingReplayResult> ReplayAsync(Guid envelopeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (envelopeId == Guid.Empty)
            return new(envelopeId, false, "MessageIdRequired");
        var letter = await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(envelopeId);
        if (letter is null || letter.Id != envelopeId)
            return new(envelopeId, false, "DeadLetterNotFound");
        if (!AllowedTypes.Contains(letter.MessageType))
            return new(envelopeId, false, "MessageTypeNotAllowed");
        var aggregateId = await AggregateIdAsync(letter.Envelope, ct);
        if (aggregateId is null)
            return new(envelopeId, false, "MessageCorrelationUnavailable");
        var validation = await reconciler.ValidateAsync(aggregateId.Value, ct);
        var reconcile = letter.MessageType == typeof(ReconcileOrderReadModel).FullName;
        if (
            validation.Issues.Any(x =>
                !reconcile || x.Code is not ("ProjectionMissing" or "CheckpointBehind")
            )
        )
            return new(envelopeId, false, "ProjectionRepairRequired");
        // MessageIds takes precedence over every broad filter in Wolverine 6.17.0.
        // Never use discard/edit or change the inbox acknowledgement.
        await runtime.Storage.DeadLetters.ReplayAsync(
            new DeadLetterEnvelopeQuery([envelopeId]),
            ct
        );
        var marked = await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(envelopeId);
        return marked?.Replayable == true
            ? new(envelopeId, true, "MarkedReplayable")
            : new(envelopeId, false, "ReplayDispositionChangedInspectRequired");
    }

    private async Task<Guid?> AggregateIdAsync(Envelope envelope, CancellationToken ct)
    {
        if (
            envelope.MessageType is null
            || !AllowedTypes.Contains(envelope.MessageType)
            || envelope.Data is null
        )
            return null;
        try
        {
            using var body = JsonDocument.Parse(envelope.Data);
            var isWebhook = envelope.MessageType == typeof(ProcessDuffelWebhookCommand).FullName;
            var name = isWebhook ? "InboxId" : "AggregateId";
            var property = body
                .RootElement.EnumerateObject()
                .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (
                property.Value.ValueKind != JsonValueKind.String
                || !property.Value.TryGetGuid(out var id)
                || id == Guid.Empty
            )
                return null;
            if (!isWebhook)
                return id;
            var entry = await db
                .WebhookInbox.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (entry is null)
                return null;
            using var payload = JsonDocument.Parse(entry.RawPayload);
            if (
                !payload.RootElement.TryGetProperty("object", out var value)
                || !value.TryGetProperty("id", out var providerId)
            )
                return null;
            var providerOrderId = providerId.GetString();
            return await db
                .Orders.AsNoTracking()
                .Where(x => x.ProviderOrderId == providerOrderId)
                .Select(x => (Guid?)x.AggregateId)
                .SingleOrDefaultAsync(ct);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
