using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Infrastructure.Cache;

public sealed class SearchCacheRedis(IConnectionMultiplexer redis, ILogger<SearchCacheRedis> log)
    : ISearchCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<Offer>?> TryGetAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(key);

        if (!value.HasValue)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<Offer>>(value.ToString(), SerializerOptions);
        }
        catch (JsonException ex)
        {
            log.LogWarning(
                ex,
                "Search cache entry for key {Key} is corrupt or uses an old schema; treating as cache miss",
                key
            );
            return null;
        }
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
