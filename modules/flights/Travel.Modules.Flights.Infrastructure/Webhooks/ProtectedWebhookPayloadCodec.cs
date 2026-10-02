using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Infrastructure.Privacy;

namespace Travel.Modules.Flights.Infrastructure.Webhooks;

public sealed record DecodedWebhookPayload(byte[] Bytes, bool Protected);

public sealed class ProtectedWebhookPayloadCodec(FlightsPiiProtectionProvider provider)
{
    public const string Format = "travel.flights.webhook.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record StoredEnvelope(
        string Format,
        Guid InboxId,
        string Source,
        string EventId,
        string EventType,
        string? ProviderOrderId,
        string Ciphertext
    );

    private static string[] Purposes(
        Guid id,
        string source,
        string eventId,
        string eventType,
        string? order
    ) =>
        [
            Format,
            id.ToString("N"),
            source,
            eventId,
            eventType,
            order is null ? "absent" : "present",
            order ?? "-",
        ];

    public ErrorOr<string> Protect(
        Guid inboxId,
        string source,
        string eventId,
        string eventType,
        byte[] bytes
    )
    {
        if (
            inboxId == Guid.Empty
            || string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(eventId)
            || string.IsNullOrWhiteSpace(eventType)
        )
            return PiiProtectionErrors.InvalidEnvelope;
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (!Matches(json.RootElement, eventId, eventType))
                return PiiProtectionErrors.InvalidEnvelope;
            var order = ProviderOrderId(json.RootElement, eventType);
            var encrypted = provider.Protect(
                bytes,
                Purposes(inboxId, source, eventId, eventType, order)
            );
            return encrypted.IsError
                ? encrypted.Errors
                : JsonSerializer.Serialize(
                    new StoredEnvelope(
                        Format,
                        inboxId,
                        source,
                        eventId,
                        eventType,
                        order,
                        Convert.ToBase64String(encrypted.Value)
                    ),
                    JsonOptions
                );
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return PiiProtectionErrors.InvalidEnvelope;
        }
    }

    public ErrorOr<DecodedWebhookPayload> Decode(WebhookInboxEntry entry)
    {
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(entry.StoredPayload);
        }
        catch (JsonException)
        {
            return new DecodedWebhookPayload(Encoding.UTF8.GetBytes(entry.StoredPayload), false);
        }
        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out _))
            {
                if (HasEnvelopeMember(root))
                    return PiiProtectionErrors.InvalidEnvelope;
                return new DecodedWebhookPayload(
                    Encoding.UTF8.GetBytes(entry.StoredPayload),
                    false
                );
            }
            try
            {
                var envelope = json.Deserialize<StoredEnvelope>(JsonOptions);
                if (
                    envelope is null
                    || envelope.Format != Format
                    || envelope.InboxId != entry.Id
                    || envelope.Source != entry.Source
                    || envelope.EventId != entry.EventId
                    || envelope.EventType != entry.EventType
                    || string.IsNullOrWhiteSpace(envelope.Ciphertext)
                )
                    return PiiProtectionErrors.InvalidEnvelope;
                var decrypted = provider.Unprotect(
                    Convert.FromBase64String(envelope.Ciphertext),
                    Purposes(
                        entry.Id,
                        entry.Source,
                        entry.EventId,
                        entry.EventType,
                        envelope.ProviderOrderId
                    )
                );
                if (decrypted.IsError)
                    return decrypted.Errors;
                var handedOff = false;
                try
                {
                    using var original = JsonDocument.Parse(decrypted.Value);
                    if (
                        !Matches(original.RootElement, entry.EventId, entry.EventType)
                        || ProviderOrderId(original.RootElement, entry.EventType)
                            != envelope.ProviderOrderId
                    )
                        return PiiProtectionErrors.InvalidEnvelope;
                    handedOff = true;
                    return new DecodedWebhookPayload(decrypted.Value, true);
                }
                finally
                {
                    if (!handedOff)
                        CryptographicOperations.ZeroMemory(decrypted.Value);
                }
            }
            catch (Exception ex)
                when (ex
                        is JsonException
                            or FormatException
                            or InvalidOperationException
                            or ArgumentException
                )
            {
                return PiiProtectionErrors.InvalidEnvelope;
            }
        }
    }

    public static string? RoutingOrderId(WebhookInboxEntry entry)
    {
        try
        {
            using var doc = JsonDocument.Parse(entry.StoredPayload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("format", out _))
                return HasEnvelopeMember(doc.RootElement)
                    ? null
                    : ProviderOrderId(doc.RootElement, entry.EventType);
            var envelope = doc.Deserialize<StoredEnvelope>(JsonOptions);
            return
                envelope is not null
                && envelope.Format == Format
                && envelope.InboxId == entry.Id
                && envelope.Source == entry.Source
                && envelope.EventId == entry.EventId
                && envelope.EventType == entry.EventType
                ? envelope.ProviderOrderId
                : null;
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // Reserved storage names distinguish corrupt protected rows from legacy wire JSON.
    private static readonly HashSet<string> EnvelopeMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "format",
        "inboxId",
        "eventId",
        "eventType",
        "providerOrderId",
        "ciphertext",
    };

    private static bool HasEnvelopeMember(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.EnumerateObject().Any(property => EnvelopeMembers.Contains(property.Name));

    internal static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Matches(JsonElement root, string eventId, string eventType) =>
        Text(root, "id") == eventId && Text(root, "type") == eventType;

    internal static string? ProviderOrderId(JsonElement root, string eventType)
    {
        if (
            eventType
            is not (
                "order.created"
                or "order.airline_initiated_change"
                or "order.airline_initiated_change.cancelled"
            )
        )
            return null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("object", out var value))
            return null;
        var id = Text(value, "id");
        return string.IsNullOrEmpty(id) ? null : id;
    }
}
