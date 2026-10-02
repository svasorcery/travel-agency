using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Webhooks;

public sealed class DuffelWebhookPayloadReader(ProtectedWebhookPayloadCodec codec)
    : IWebhookPayloadReader
{
    private static BookingWebhookFacts Ignored => new(BookingWebhookKind.Ignored, null, new([]));

    public ErrorOr<BookingWebhookFacts> Read(WebhookInboxEntry entry)
    {
        if (entry.ProcessedAt is not null)
            return Ignored;
        var decoded = codec.Decode(entry);
        if (decoded.IsError)
            return decoded.Errors;
        try
        {
            using var doc = JsonDocument.Parse(decoded.Value.Bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return PiiProtectionErrors.InvalidEnvelope;
            if (!root.TryGetProperty("object", out var obj))
                return Ignored;
            if (entry.EventType == "order.airline_initiated_change")
                return new BookingWebhookFacts(BookingWebhookKind.AirlineChanged, null, new([]));
            if (
                entry.EventType
                is not ("order.created" or "order.airline_initiated_change.cancelled")
            )
                return Ignored;
            if (!obj.TryGetProperty("id", out var id))
                return Ignored;
            var orderId = id.GetString();
            if (string.IsNullOrEmpty(orderId))
                return Ignored;
            if (entry.EventType == "order.airline_initiated_change.cancelled")
                return new BookingWebhookFacts(
                    BookingWebhookKind.AirlineCancelled,
                    orderId,
                    new([])
                );
            if (
                !obj.TryGetProperty("documents", out var documents)
                || documents.ValueKind != JsonValueKind.Array
            )
                return Ignored;
            var tickets = new List<string>();
            foreach (var document in documents.EnumerateArray())
            {
                if (
                    document.TryGetProperty("type", out var type)
                    && type.GetString() == "ticket"
                    && document.TryGetProperty("unique_identifier", out var identifier)
                )
                {
                    var number = identifier.GetString();
                    if (!string.IsNullOrEmpty(number))
                        tickets.Add(number);
                }
            }
            return tickets.Count == 0
                ? Ignored
                : new BookingWebhookFacts(
                    BookingWebhookKind.TicketsAvailable,
                    orderId,
                    new EquatableArray<string>(tickets.ToArray())
                );
        }
        catch (JsonException)
        {
            return decoded.Value.Protected ? PiiProtectionErrors.InvalidEnvelope : Ignored;
        }
        catch (InvalidOperationException)
        {
            return PiiProtectionErrors.InvalidEnvelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded.Value.Bytes);
        }
    }
}
