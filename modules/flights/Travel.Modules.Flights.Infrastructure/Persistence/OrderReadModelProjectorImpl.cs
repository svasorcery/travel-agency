using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

/// <summary>
/// EF-backed implementation of IOrderReadModelProjector.
/// Lives in Infrastructure so that Application avoids referencing the EF DbContext directly
/// (which would create a circular project dependency since Infrastructure references Application).
///
/// All timestamps are sourced from the aggregate's event-driven fields so the projection is
/// idempotent and replay-stable; the <see cref="TimeProvider"/> parameter remains for any
/// future fields that genuinely require projection-time but is currently unused for stamping.
/// </summary>
public sealed class OrderReadModelProjectorImpl(FlightsDbContext db) : IOrderReadModelProjector
{
    public async Task Project(
        BookingAggregate agg,
        Guid userId,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var entity = await db.Orders.FirstOrDefaultAsync(o => o.AggregateId == agg.Id, ct);
        if (entity is null)
        {
            entity = new OrderReadModelEntity
            {
                Id = Guid.NewGuid(),
                AggregateId = agg.Id,
                UserId = userId,
            };
            db.Orders.Add(entity);
        }

        entity.Status = agg.Status.ToString();
        entity.ProviderOrderId = agg.ProviderOrderId;
        entity.TotalAmount = agg.TotalAmount!.Amount;
        entity.Currency = agg.TotalAmount!.Currency.Value;
        entity.ItineraryJson = JsonSerializer.Serialize(
            agg.Itinerary,
            JsonSerializerOptions.Default
        );
        entity.PassengerInfoJson = agg.Passenger is null
            ? "null"
            : JsonSerializer.Serialize(agg.Passenger, JsonSerializerOptions.Default);
        entity.TicketNumbers = agg.TicketNumbers.ToArray();

        // Timestamps come from the event payloads via BookingAggregate — the projection
        // is replay-stable and the projector's wall clock no longer leaks into the
        // read model. BookedAt falls back to ConfirmedAt for streams that skipped
        // OfferHeld (defensive — none in M1 but it costs nothing).
        entity.BookedAt = agg.BookedAt ?? agg.ConfirmedAt ?? entity.BookedAt;
        entity.TicketedAt = agg.TicketedAt;
        entity.CancelledAt = agg.CancelledAt;
        entity.RefundedAt = agg.RefundedAt;

        await db.SaveChangesAsync(ct);
    }
}
