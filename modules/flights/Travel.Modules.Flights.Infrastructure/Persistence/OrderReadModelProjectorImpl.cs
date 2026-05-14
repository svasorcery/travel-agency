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
        entity.BookedAt = entity.BookedAt == default ? time.GetUtcNow() : entity.BookedAt;

        if (agg.Status == BookingStatus.Ticketed)
            entity.TicketedAt = time.GetUtcNow();
        if (agg.Status == BookingStatus.Cancelled)
            entity.CancelledAt = time.GetUtcNow();
        if (agg.Status == BookingStatus.Refunded)
            entity.RefundedAt = time.GetUtcNow();

        await db.SaveChangesAsync(ct);
    }
}
