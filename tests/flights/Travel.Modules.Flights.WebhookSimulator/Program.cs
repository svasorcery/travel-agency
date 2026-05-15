using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// WebhookSimulator — a minimal-API utility (spec §17.2) that builds a Duffel-format
// webhook payload and signs it with the scheme from DuffelWebhookVerifier (WS3 Task 3.1).
//
// Usage:
//   POST /simulate?orderId=<id>&eventType=order.created.documents_issued
//   Header: X-Webhook-Secret: <secret>
//   Returns: { "payload": "<json>", "signature": "t=...,v1=..." }
//
// The simulator is intended for integration tests and local development only.
// It is NOT registered as an Aspire resource; tests reference the static factory method directly.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost(
    "/simulate",
    (string orderId, string eventType, string? ticketNumber, HttpRequest req) =>
    {
        var secret = req.Headers["X-Webhook-Secret"].FirstOrDefault() ?? string.Empty;
        var (payload, signature) = WebhookSimulatorFactory.BuildSignedWebhook(
            orderId,
            eventType,
            ticketNumber ?? "TKT-0001",
            secret
        );
        return Results.Ok(new { payload, signature });
    }
);

app.Run();

/// <summary>
/// Factory that builds a Duffel-format signed webhook payload.  Extracted as a static class so
/// that integration tests can call it without starting the HTTP server.
/// </summary>
public static class WebhookSimulatorFactory
{
    /// <summary>
    /// Builds a Duffel-format JSON payload for the given event and produces the
    /// <c>X-Duffel-Signature</c> header value in <c>t=&lt;unix&gt;,v1=&lt;hex&gt;</c> format.
    /// </summary>
    /// <param name="orderId">Duffel order id (e.g. "ord_0001").</param>
    /// <param name="eventType">Duffel event type (e.g. "order.created.documents_issued").</param>
    /// <param name="ticketNumber">Ticket number to embed in a documents-issued payload.</param>
    /// <param name="webhookSecret">The shared webhook secret known to DuffelWebhookVerifier.</param>
    /// <param name="unixSeconds">Unix timestamp to embed (defaults to current time).</param>
    public static (string Payload, string Signature) BuildSignedWebhook(
        string orderId,
        string eventType,
        string ticketNumber,
        string webhookSecret,
        long? unixSeconds = null
    )
    {
        var ts = unixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var documentObject = new
        {
            id = orderId,
            documents = new[]
            {
                new { unique_identifier = ticketNumber, type = "electronic_ticket" },
            },
        };

        var envelope = new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = eventType,
            created_at = DateTimeOffset.UtcNow,
            @object = documentObject,
        };

        var payload = JsonSerializer.Serialize(envelope);
        var signature = ComputeSignature(Encoding.UTF8.GetBytes(payload), webhookSecret, ts);
        return (payload, signature);
    }

    // Mirrors the scheme in DuffelWebhookVerifier: HMAC-SHA256("<ts>.<body>") as lowercase hex,
    // formatted as "t=<ts>,v1=<hex>".
    private static string ComputeSignature(byte[] body, string secret, long unixSeconds)
    {
        var ts = unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var tsBytes = Encoding.UTF8.GetBytes(ts);
        var signed = new byte[tsBytes.Length + 1 + body.Length];
        Buffer.BlockCopy(tsBytes, 0, signed, 0, tsBytes.Length);
        signed[tsBytes.Length] = (byte)'.';
        Buffer.BlockCopy(body, 0, signed, tsBytes.Length + 1, body.Length);

        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, signed);
        return $"t={ts},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
