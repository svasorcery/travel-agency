using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class DuffelWebhookEndpoint
{
    /// <summary>
    /// Ingests a Duffel webhook delivery.
    /// </summary>
    /// <remarks>
    /// The inbox INSERT and the outgoing <see cref="ProcessDuffelWebhookCommand"/> are
    /// committed atomically by <see cref="IDbContextOutbox{TDbContext}.SaveChangesAndFlushMessagesAsync"/>:
    /// the publish is buffered on the EF-enrolled outbox, and the buffered messages flush
    /// inside the same DbContext transaction as <c>db.SaveChangesAsync</c>. A concurrent
    /// duplicate delivery hits the <c>uq(source,event_id)</c> index and raises Postgres
    /// SqlState <c>23505</c>; we treat that as a successful dedup and return 200.
    ///
    /// <para>We deliberately do NOT use <c>[Transactional]</c> middleware here: under that
    /// middleware a unique-violation aborts the surrounding Wolverine transaction, leaving
    /// no clean way to translate the failure into a 2xx response. The
    /// <see cref="IDbContextOutbox{TDbContext}"/> primitive gives the same
    /// inbox-insert-plus-outbox-publish atomicity in a single explicit call, while letting
    /// the endpoint catch the 23505 and respond OK.</para>
    /// </remarks>
    [WolverinePost("/webhooks/duffel")]
    [AllowAnonymous]
    public static async Task<IResult> Receive(
        HttpRequest req,
        DuffelWebhookVerifier verifier,
        FlightsDbContext db,
        IDbContextOutbox<FlightsDbContext> outbox,
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<DuffelWebhookEndpoint> log,
        CancellationToken ct
    )
    {
        req.EnableBuffering();
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms, ct);
        var raw = ms.ToArray();

        // Duffel sends the signature in the `X-Duffel-Signature` header
        // (see https://duffel.com/docs/guides/receiving-webhooks).
        if (
            !req.Headers.TryGetValue("X-Duffel-Signature", out var sig)
            || !verifier.Verify(raw, sig!)
        )
            return Results.Unauthorized();

        DuffelWebhookEventDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DuffelWebhookEventDto>(
                raw,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Duffel webhook: failed to deserialize payload.");
            return Results.BadRequest();
        }

        if (dto is null || string.IsNullOrEmpty(dto.Id))
            return Results.BadRequest();

        // Fast path: sequential duplicates (the common case — Duffel retries on any
        // non-2xx) are caught by a pre-check so we avoid a failed INSERT + rollback.
        // The 23505 catch below is the real correctness guarantee for the concurrent
        // race that the pre-check cannot rule out.
        var existing = await db
            .WebhookInbox.AsNoTracking()
            .AnyAsync(x => x.Source == "duffel" && x.EventId == dto.Id, ct);
        if (existing)
            return Results.Ok();

        var row = new WebhookInboxEntity
        {
            Id = Guid.NewGuid(),
            Source = "duffel",
            EventId = dto.Id,
            EventType = dto.Type,
            RawPayload = Encoding.UTF8.GetString(raw),
            Signature = sig!,
            ReceivedAt = time.GetUtcNow(),
        };
        db.WebhookInbox.Add(row);

        // Buffer the follow-up command on the EF-enrolled outbox. It is NOT sent yet —
        // SaveChangesAndFlushMessagesAsync below saves the inbox row and flushes the
        // buffered envelopes inside one DbContext transaction.
        await outbox.PublishAsync(new ProcessDuffelWebhookCommand(row.Id));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // A concurrent delivery for the same (source, event_id) beat us to the
            // unique index. The other request's transaction already committed its
            // inbox row AND its ProcessDuffelWebhookCommand, so it is idempotent for
            // this caller to return 200 without inserting a row.
            db.Entry(row).State = EntityState.Detached;
            return Results.Ok();
        }
        catch (PostgresException pgex) when (pgex.SqlState == "23505")
        {
            // Defensive: if the unique-violation surfaces as a raw Npgsql exception
            // without the DbUpdateException wrapper (depending on EF/Wolverine paths),
            // treat it identically.
            db.Entry(row).State = EntityState.Detached;
            return Results.Ok();
        }

        metrics.RecordWebhookReceived(dto.Type);
        return Results.Ok();
    }
}
