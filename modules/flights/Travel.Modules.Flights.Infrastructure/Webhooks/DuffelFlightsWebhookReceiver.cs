using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Webhooks;

public sealed class DuffelFlightsWebhookReceiver(
    DuffelWebhookVerifier verifier,
    IWebhookIngestionPort ingestion
) : IRawWebhookReceiver
{
    public string Name => "flights";

    public RawWebhookProbe Probe(RawWebhookDelivery delivery)
    {
        if (!verifier.Verify(delivery.Bytes.ToArray(), delivery.Signature))
            return new(RawWebhookProbeKind.InvalidSignature);
        if (!verifier.IsCurrentPayload(delivery.Bytes))
            return new(RawWebhookProbeKind.InvalidPayload);
        using var json = JsonDocument.Parse(delivery.Bytes);
        var type = json.RootElement.GetProperty("type").GetString();
        return type switch
        {
            "order.created" or "order.airline_initiated_change_detected" => new(
                RawWebhookProbeKind.Owner
            ),
            "stays.booking.created"
            or "stays.booking_creation_failed"
            or "stays.booking.cancelled" => new(RawWebhookProbeKind.RequiredReceiver, "hotels"),
            _ => new(RawWebhookProbeKind.NotApplicable),
        };
    }

    public async Task<RawWebhookAcceptance> AcceptAsync(
        RawWebhookDelivery delivery,
        CancellationToken cancellationToken
    )
    {
        if (Probe(delivery).Kind != RawWebhookProbeKind.Owner)
            return RawWebhookAcceptance.InvalidPayload;
        try
        {
            var result = await ingestion.IngestAsync(
                new(
                    "duffel",
                    delivery.Bytes,
                    new Dictionary<string, string> { ["X-Duffel-Signature"] = delivery.Signature }
                ),
                cancellationToken
            );
            return result.IsError
                ? result.FirstError.Type == ErrorType.Unauthorized
                    ? RawWebhookAcceptance.InvalidSignature
                    : result.FirstError.Type == ErrorType.Validation
                        ? RawWebhookAcceptance.InvalidPayload
                        : RawWebhookAcceptance.Unavailable
                : result.Value == WebhookIngestionOutcome.Duplicate
                    ? RawWebhookAcceptance.Duplicate
                    : RawWebhookAcceptance.Accepted;
        }
        catch (Exception ex)
            when (ex
                    is System.Data.Common.DbException
                        or Microsoft.EntityFrameworkCore.DbUpdateException
                        or InvalidOperationException
            )
        {
            return RawWebhookAcceptance.Unavailable;
        }
    }
}
