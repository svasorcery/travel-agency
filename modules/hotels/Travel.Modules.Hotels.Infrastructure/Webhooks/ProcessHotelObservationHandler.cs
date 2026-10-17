using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;

namespace Travel.Modules.Hotels.Infrastructure.Webhooks;

public sealed class ProcessHotelObservationHandler
{
    public static async Task Handle(
        ProcessHotelObservation message,
        HotelsDbContext db,
        HotelPiiProtectionProvider protector,
        HotelReservationService service,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var inbox = await db.Inbox.SingleOrDefaultAsync(i => i.Id == message.InboxId, ct);
        if (inbox is null || inbox.ProcessedAt is not null || inbox.ProtectedPayload is null)
            return;
        var decoded = protector.Unprotect(
            Convert.FromBase64String(inbox.ProtectedPayload),
            "hotels-webhook.v1",
            inbox.Receiver,
            "Test",
            inbox.Id.ToString("N"),
            inbox.EventId,
            inbox.EventType
        );
        if (decoded.IsError)
            throw new InvalidOperationException("Hotels observation protection unavailable.");
        try
        {
            using var json = JsonDocument.Parse(decoded.Value);
            var obj = json.RootElement.GetProperty("data").GetProperty("object");
            var reference = inbox.PreparationReference;
            var bookingId =
                DuffelStaysWebhookReceiver.Text(obj, "booking_id")
                ?? (
                    inbox.EventType == "stays.booking.cancelled"
                        ? DuffelStaysWebhookReceiver.Text(obj, "id")
                        : null
                );
            HotelReservationEntity? row = null;
            if (reference is not null)
                row = await db
                    .Reservations.AsNoTracking()
                    .SingleOrDefaultAsync(
                        r =>
                            r.Provider == "duffel-stays"
                            && r.ProviderAccount == inbox.Receiver
                            && r.Environment == "Test"
                            && r.SourcePreparationReference == reference
                            && r.AttemptId != null,
                        ct
                    );
            else if (ReservationState.SafeReference(bookingId))
                row = await db
                    .Reservations.AsNoTracking()
                    .SingleOrDefaultAsync(
                        r =>
                            r.Provider == "duffel-stays"
                            && r.ProviderAccount == inbox.Receiver
                            && r.Environment == "Test"
                            && r.BookingReference == bookingId
                            && r.AttemptId != null,
                        ct
                    );
            // No heuristic fallback. An unknown source quote remains bounded Hotels-owned evidence.
            if (row is null)
            {
                inbox.Unmatched = true;
                inbox.ProcessedAt = time.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return;
            }
            var kind =
                inbox.EventType == "stays.booking_creation_failed" ? ObservationKind.NotCreated
                : inbox.EventType == "stays.booking.cancelled" ? ObservationKind.Cancelled
                : ObservationKind.ReservedWithoutDetails;
            var observation = new BookingObservation(
                kind,
                ReservationState.SafeReference(bookingId) ? new(bookingId!, null, null) : null,
                SenderFinished: false,
                Diagnostic: kind == ObservationKind.NotCreated ? "SourceNotCreated" : null
            );
            var result = await service.ApplyAsync(row.Id, observation, false, ct);
            if (result.IsError)
                throw new InvalidOperationException("Hotels observation could not be saved.");
            // A reservation CAS retry may clear EF tracking. Persist this marker independently of
            // the original tracked inbox instance; a failed write still fails the processing message.
            await db
                .Inbox.Where(i => i.Id == inbox.Id && i.ProcessedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProcessedAt, time.GetUtcNow()), ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded.Value);
        }
    }
}
