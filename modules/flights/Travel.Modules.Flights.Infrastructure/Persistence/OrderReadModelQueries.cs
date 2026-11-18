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

    public async Task<IReadOnlyList<ProjectedFlightOrderCandidate>> ListCandidatesAsync(
        Guid ownerId,
        int limit,
        int offset,
        CancellationToken ct
    ) => await CandidateQuery(ownerId, limit, offset).ToListAsync(ct);

    internal IQueryable<ProjectedFlightOrderCandidate> CandidateQuery(
        Guid ownerId,
        int limit,
        int offset
    ) =>
        db
            .Orders.FromSql(
                $"""
                SELECT aggregate_id, user_id, status, passenger_count, booked_at,
                       CASE WHEN octet_length(itinerary_json::text) <= {ProjectedFlightOrderCandidate.MaximumItineraryBytes}
                            THEN itinerary_json ELSE 'null'::jsonb END AS itinerary_json
                FROM flights.order_read_model
                WHERE user_id = {ownerId} AND status <> 'OfferQuoted'
                """
            )
            .OrderByDescending(o => o.BookedAt)
            .ThenByDescending(o => o.AggregateId)
            .Skip(offset)
            .Take(limit)
            .Select(o => new ProjectedFlightOrderCandidate(
                o.AggregateId,
                o.UserId,
                o.Status,
                o.PassengerCount,
                o.ItineraryJson
            ));

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
