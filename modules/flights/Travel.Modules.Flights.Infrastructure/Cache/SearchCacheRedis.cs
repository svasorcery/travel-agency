using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;

namespace Travel.Modules.Flights.Infrastructure.Cache;

public sealed class SearchCacheRedis(IConnectionMultiplexer redis, ILogger<SearchCacheRedis> log)
    : ISearchCache
{
    private sealed record CacheEnvelope(int SchemaVersion, SearchResult Result);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SearchResult?> TryGetAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(key);

        if (!value.HasValue)
            return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(
                value.ToString(),
                SerializerOptions
            );
            var result = envelope?.Result;
            return
                envelope?.SchemaVersion == 2
                && result is not null
                && result.PartialFailures is not null
                && result.PartialFailures.All(f =>
                    f is not null
                    && !string.IsNullOrWhiteSpace(f.Provider)
                    && !string.IsNullOrWhiteSpace(f.ErrorCode)
                    && f.ElapsedMs >= 0
                )
                && OfferRanker.IsValid(result.Ranking, result.Offers)
                ? result
                : null;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or NotSupportedException
                        or ArgumentException
                        or NullReferenceException
                        or InvalidOperationException
                        or OverflowException
            )
        {
            log.LogWarning(
                "Search cache entry for key {Key} is corrupt or uses an old schema; treating as cache miss",
                key
            );
            return null;
        }
    }

    public async Task SetAsync(string key, SearchResult result, TimeSpan ttl, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var json = JsonSerializer.Serialize(new CacheEnvelope(2, result), SerializerOptions);
        await db.StringSetAsync(key, json, ttl);
    }
}
