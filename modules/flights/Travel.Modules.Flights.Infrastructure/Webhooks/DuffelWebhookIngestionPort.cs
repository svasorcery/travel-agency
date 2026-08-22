using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Wolverine.EntityFrameworkCore;

namespace Travel.Modules.Flights.Infrastructure.Webhooks;

public sealed class DuffelWebhookIngestionPort(
    DuffelWebhookVerifier verifier,
    FlightsDbContext db,
    IDbContextOutbox<FlightsDbContext> outbox,
    IFlightsMetrics metrics,
    TimeProvider time,
    ILogger<DuffelWebhookIngestionPort> log
) : IWebhookIngestionPort
{
    private const string Provider = "duffel";
    private const string SignatureHeader = "X-Duffel-Signature";
    private const string WebhookUniqueIndex = "ix_webhook_inbox_source_event_id";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
        WebhookIngestionRequest request,
        CancellationToken ct
    )
    {
        var payload = request.Payload.ToArray();
        if (
            !request.Headers.TryGetValue(SignatureHeader, out var signature)
            || !verifier.Verify(payload, signature)
        )
            return WebhookIngestionErrors.InvalidSignature;

        DuffelWebhookEventDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DuffelWebhookEventDto>(payload, JsonOptions);
        }
        catch (JsonException exception)
        {
            log.LogWarning(exception, "Duffel webhook: failed to deserialize payload.");
            return WebhookIngestionErrors.InvalidPayload;
        }

        if (dto is null || string.IsNullOrEmpty(dto.Id))
            return WebhookIngestionErrors.InvalidPayload;

        var exists = await db
            .WebhookInbox.AsNoTracking()
            .AnyAsync(row => row.Source == Provider && row.EventId == dto.Id, ct);
        if (exists)
            return WebhookIngestionOutcome.Duplicate;

        var inbox = new WebhookInboxEntity
        {
            Id = Guid.NewGuid(),
            Source = Provider,
            EventId = dto.Id,
            EventType = dto.Type,
            RawPayload = Encoding.UTF8.GetString(payload),
            Signature = signature,
            ReceivedAt = time.GetUtcNow(),
        };
        db.WebhookInbox.Add(inbox);

        await outbox.PublishAsync(new ProcessDuffelWebhookCommand(inbox.Id));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && IsWebhookDuplicate(postgres)
            )
        {
            db.Entry(inbox).State = EntityState.Detached;
            return WebhookIngestionOutcome.Duplicate;
        }
        catch (PostgresException exception) when (IsWebhookDuplicate(exception))
        {
            log.LogWarning(
                "Duffel webhook dedup matched via bare PostgresException; expected DbUpdateException."
            );
            db.Entry(inbox).State = EntityState.Detached;
            return WebhookIngestionOutcome.Duplicate;
        }

        metrics.RecordWebhookReceived(dto.Type);
        return WebhookIngestionOutcome.Accepted;
    }

    private static bool IsWebhookDuplicate(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation
        && exception.ConstraintName == WebhookUniqueIndex;
}
