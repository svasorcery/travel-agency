using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class DuffelWebhookEndpoint
{
    [WolverinePost("/webhooks/duffel")]
    [AllowAnonymous]
    public static async Task<IResult> Receive(
        HttpRequest req,
        DuffelWebhookVerifier verifier,
        FlightsDbContext db,
        IFlightsMetrics metrics,
        IMessageBus bus,
        TimeProvider time,
        ILogger<DuffelWebhookEndpoint> log,
        CancellationToken ct
    )
    {
        req.EnableBuffering();
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms, ct);
        var raw = ms.ToArray();

        if (
            !req.Headers.TryGetValue("Duffel-Signature", out var sig) || !verifier.Verify(raw, sig!)
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

        // Deduplication: check if we already have this event
        var existing = await db.WebhookInbox.FirstOrDefaultAsync(
            x => x.Source == "duffel" && x.EventId == dto.Id,
            ct
        );
        if (existing is not null)
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
        await db.SaveChangesAsync(ct);

        metrics.RecordWebhookReceived(dto.Type);
        await bus.PublishAsync(new ProcessDuffelWebhookCommand(row.Id));
        return Results.Ok();
    }
}
