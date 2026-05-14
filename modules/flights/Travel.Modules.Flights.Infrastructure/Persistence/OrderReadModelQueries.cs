using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

/// <summary>
/// EF-backed implementation of IOrderReadModelQueries.
/// Lives in Infrastructure so that Application avoids referencing FlightsDbContext directly
/// (which would create a circular project dependency since Infrastructure references Application).
/// </summary>
public sealed class OrderReadModelQueries(FlightsDbContext db) : IOrderReadModelQueries
{
    private const int MinLimit = 1;
    private const int MaxLimit = 200;

    public async Task<OrderView?> GetAsync(Guid aggregateId, Guid userId, CancellationToken ct)
    {
        var entity = await db
            .Orders.Where(o => o.AggregateId == aggregateId && o.UserId == userId)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : MapToView(entity);
    }

    public async Task<OrderListView> ListAsync(
        Guid userId,
        int limit,
        int offset,
        CancellationToken ct
    )
    {
        var clampedLimit = Math.Clamp(limit, MinLimit, MaxLimit);

        var items = await db
            .Orders.Where(o => o.UserId == userId)
            .OrderByDescending(o => o.BookedAt)
            .Skip(offset)
            .Take(clampedLimit)
            .ToListAsync(ct);

        return new OrderListView(items.Select(MapToView).ToList(), clampedLimit, offset);
    }

    private static OrderView MapToView(OrderReadModelEntity e) =>
        new(
            AggregateId: e.AggregateId,
            UserId: e.UserId,
            ProviderOrderId: e.ProviderOrderId,
            Status: e.Status,
            TotalAmount: e.TotalAmount,
            Currency: e.Currency,
            ItineraryJson: e.ItineraryJson,
            PassengerInfoJson: e.PassengerInfoJson,
            TicketNumbers: e.TicketNumbers,
            BookedAt: e.BookedAt,
            TicketedAt: e.TicketedAt,
            CancelledAt: e.CancelledAt,
            RefundedAt: e.RefundedAt
        );
}
