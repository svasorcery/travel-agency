using System.Text.Json;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Infrastructure.Cache;

public sealed class SearchCacheRedis(IConnectionMultiplexer redis) : ISearchCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IReadOnlyList<Offer>?> TryGetAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(key);

        if (!value.HasValue)
            return null;

        return JsonSerializer.Deserialize<List<Offer>>(value.ToString(), SerializerOptions);
    }

    public async Task SetAsync(
        string key,
        IReadOnlyList<Offer> offers,
        TimeSpan ttl,
        CancellationToken ct
    )
    {
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(offers, SerializerOptions);
        await db.StringSetAsync(key, json, ttl);
    }
}
