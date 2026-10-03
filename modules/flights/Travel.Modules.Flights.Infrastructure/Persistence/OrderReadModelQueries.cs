using System.Linq.Expressions;
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
            .Select(Projection)
            .FirstOrDefaultAsync(ct);

        return entity;
    }

    public async Task<OrderListView> ListAsync(
        Guid userId,
        int limit,
        int offset,
        CancellationToken ct
    )
    {
        var clampedLimit = Math.Clamp(limit, MinLimit, MaxLimit);
        var safeOffset = Math.Max(0, offset);

        var items = await db
            .Orders.Where(o => o.UserId == userId)
            .OrderByDescending(o => o.BookedAt)
            .ThenByDescending(o => o.AggregateId)
            .Skip(safeOffset)
            .Take(clampedLimit)
            .Select(Projection)
            .ToListAsync(ct);

        return new OrderListView(items, clampedLimit, safeOffset);
    }

    private static readonly Expression<Func<OrderReadModelEntity, OrderView>> Projection =
        e => new OrderView(
            e.AggregateId,
            e.UserId,
            e.ProviderOrderId,
            e.Status,
            e.TotalAmount,
            e.Currency,
            e.ItineraryJson,
            e.TicketNumbers,
            e.BookedAt,
            e.TicketedAt,
            e.CancelledAt,
            e.RefundedAt,
            e.PassengerCount,
            e.ProjectedStreamVersion
        );
}
