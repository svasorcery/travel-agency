using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;
using Wolverine.EntityFrameworkCore;

namespace Travel.Modules.Hotels.Infrastructure.Webhooks;

public sealed record ProcessHotelObservation(Guid InboxId);

public sealed class DuffelStaysWebhookReceiver(
    IOptions<HotelsOptions> options,
    HotelsDbContext db,
    IDbContextOutbox<HotelsDbContext> outbox,
    HotelPiiProtectionProvider protector,
    TimeProvider time
) : IRawWebhookReceiver
{
    public string Name => "hotels";

    public RawWebhookProbe Probe(RawWebhookDelivery delivery)
    {
        if (!Verify(delivery, options.Value.WebhookSecret))
            return new(RawWebhookProbeKind.InvalidSignature);
        try
        {
            using var json = JsonDocument.Parse(delivery.Bytes);
            var root = json.RootElement;
            if (!Current(root, options.Value.OrganisationId))
                return new(RawWebhookProbeKind.InvalidPayload);
            var type = Text(root, "type");
            if (
                type
                is "stays.booking.created"
                    or "stays.booking_creation_failed"
                    or "stays.booking.cancelled"
            )
                return new(
                    options.Value.ReceiverEnabled
                        ? RawWebhookProbeKind.Owner
                        : RawWebhookProbeKind.Unavailable
                );
            if (type is "order.created" or "order.airline_initiated_change_detected")
                return new(RawWebhookProbeKind.RequiredReceiver, "flights");
            return new(RawWebhookProbeKind.NotApplicable);
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new(RawWebhookProbeKind.InvalidPayload);
        }
    }

    public async Task<RawWebhookAcceptance> AcceptAsync(
        RawWebhookDelivery delivery,
        CancellationToken cancellationToken
    )
    {
        var probe = Probe(delivery);
        if (probe.Kind != RawWebhookProbeKind.Owner)
            return probe.Kind == RawWebhookProbeKind.InvalidSignature
                    ? RawWebhookAcceptance.InvalidSignature
                : probe.Kind == RawWebhookProbeKind.InvalidPayload
                    ? RawWebhookAcceptance.InvalidPayload
                : RawWebhookAcceptance.Unavailable;
        using var json = JsonDocument.Parse(delivery.Bytes);
        var root = json.RootElement;
        var id = Text(root, "id")!;
        var type = Text(root, "type")!;
        var receiver = options.Value.OrganisationId ?? "configured-test-account";
        try
        {
            if (
                await db
                    .Inbox.AsNoTracking()
                    .AnyAsync(i => i.Receiver == receiver && i.EventId == id, cancellationToken)
            )
                return RawWebhookAcceptance.Duplicate;
            var obj = root.GetProperty("data").GetProperty("object");
            var reference = Text(obj, "quote_id");
            var inboxId = Guid.NewGuid();
            var encrypted = protector.Protect(
                delivery.Bytes.ToArray(),
                "hotels-webhook.v1",
                receiver,
                "Test",
                inboxId.ToString("N"),
                id,
                type
            );
            if (encrypted.IsError)
                return RawWebhookAcceptance.Unavailable;
            db.Inbox.Add(
                new()
                {
                    Id = inboxId,
                    Receiver = receiver,
                    EventId = id,
                    EventType = type,
                    PreparationReference = Core.ReservationState.SafeReference(reference)
                        ? reference
                        : null,
                    ProtectedPayload = Convert.ToBase64String(encrypted.Value),
                    ReceivedAt = time.GetUtcNow(),
                }
            );
            await outbox.PublishAsync(new ProcessHotelObservation(inboxId));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return RawWebhookAcceptance.Accepted;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            db.ChangeTracker.Clear();
            return RawWebhookAcceptance.Duplicate;
        }
        catch (Exception ex)
            when (ex
                    is System.Data.Common.DbException
                        or DbUpdateException
                        or InvalidOperationException
            )
        {
            db.ChangeTracker.Clear();
            return RawWebhookAcceptance.Unavailable;
        }
    }

    internal static bool Current(JsonElement root, string? organisation)
    {
        if (
            root.ValueKind != JsonValueKind.Object
            || root.TryGetProperty("object", out _)
            || Text(root, "api_version") != "v2"
            || !Core.ReservationState.SafeReference(Text(root, "id"))
            || string.IsNullOrWhiteSpace(Text(root, "type"))
            || !root.TryGetProperty("live_mode", out var mode)
            || mode.ValueKind != JsonValueKind.False
            || !DateTimeOffset.TryParse(
                Text(root, "created_at"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out _
            )
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("object", out var obj)
            || obj.ValueKind != JsonValueKind.Object
        )
            return false;
        var account = Text(root, "identity_organisation_id");
        if (account is not null && (organisation is null || account != organisation))
            return false;
        var quote = Text(obj, "quote_id");
        var booking = Text(obj, "booking_id");
        var id = Text(obj, "id");
        return Text(root, "type") switch
        {
            "stays.booking.created" => DuffelStaysMapper.PathReference(quote)
                && DuffelStaysMapper.PathReference(booking),
            "stays.booking_creation_failed" => DuffelStaysMapper.PathReference(quote),
            "stays.booking.cancelled" => DuffelStaysMapper.PathReference(booking ?? id)
                && (booking is null || id is null || booking == id)
                && (quote is null || DuffelStaysMapper.PathReference(quote)),
            _ => true,
        };
    }

    internal static string? Text(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    internal static bool Verify(RawWebhookDelivery delivery, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || delivery.Bytes.Length > 2 * 1024 * 1024)
            return false;
        string? timestamp = null,
            signature = null;
        foreach (var item in delivery.Signature.Split(','))
        {
            var pair = item.Trim().Split('=', 2);
            if (pair.Length != 2)
                return false;
            if (pair[0] == "t")
            {
                if (timestamp is not null)
                    return false;
                timestamp = pair[1];
            }
            if (pair[0] == "v1")
            {
                if (signature is not null)
                    return false;
                signature = pair[1];
            }
        }
        if (!long.TryParse(timestamp, CultureInfo.InvariantCulture, out _) || signature is null)
            return false;
        try
        {
            var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
            var signed = new byte[prefix.Length + delivery.Bytes.Length];
            prefix.CopyTo(signed, 0);
            delivery.Bytes.Span.CopyTo(signed.AsSpan(prefix.Length));
            return CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed),
                Convert.FromHexString(signature)
            );
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
