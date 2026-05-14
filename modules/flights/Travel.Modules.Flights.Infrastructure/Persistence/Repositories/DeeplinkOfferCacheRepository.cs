using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Repositories;

public sealed class DeeplinkOfferCacheRepository(FlightsDbContext db, TimeProvider time)
    : IDeeplinkOfferCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DeeplinkOffer>?> TryGetAsync(
        string criteriaHash,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        var row = await db
            .DeeplinkOffersCache.Where(x => x.CriteriaHash == criteriaHash && x.ExpiresAt > now)
            .OrderByDescending(x => x.FetchedAt)
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : JsonSerializer.Deserialize<List<DeeplinkOffer>>(row.OffersJson, Json);
    }

    public async Task SetAsync(
        string criteriaHash,
        IReadOnlyList<DeeplinkOffer> offers,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        db.DeeplinkOffersCache.Add(
            new DeeplinkOfferCacheEntity
            {
                Id = Guid.NewGuid(),
                CriteriaHash = criteriaHash,
                OffersJson = JsonSerializer.Serialize(offers, Json),
                FetchedAt = now,
                ExpiresAt = now.AddHours(1),
            }
        );
        await db.SaveChangesAsync(ct);
    }

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.DeeplinkOffersCache.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
    }
}
