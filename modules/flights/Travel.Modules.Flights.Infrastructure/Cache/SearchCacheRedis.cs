using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

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
            using var document = JsonDocument.Parse(value.ToString());
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("result", out var wireResult)
                || wireResult.ValueKind != JsonValueKind.Object
                || !wireResult.TryGetProperty("offers", out var wireOffers)
                || wireOffers.ValueKind != JsonValueKind.Array
            )
                return null;
            // Missing capability facts are an old/incomplete payload; explicit null is truthful unknown.
            foreach (var offer in wireOffers.EnumerateArray())
            {
                if (offer.ValueKind != JsonValueKind.Object)
                    return null;
                if (
                    offer.TryGetProperty("$type", out var kind)
                    && kind.GetString() == "bookable"
                    && (
                        !offer.TryGetProperty("party", out var party)
                        || party.ValueKind != JsonValueKind.Object
                        || !party.TryGetProperty("supportsHold", out _)
                        || !party.TryGetProperty("requiresIdentityDocuments", out _)
                    )
                )
                    return null;
            }
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(
                value.ToString(),
                SerializerOptions
            );
            var result = envelope?.Result;
            return
                envelope?.SchemaVersion == 4
                && result is not null
                && result.SkippedProviders is not null
                && result.SkippedProviders.All(s =>
                    s is not null
                    && !string.IsNullOrWhiteSpace(s.Provider)
                    && s.ReasonCode is "passenger-count-unsupported" or "journey-unsupported"
                )
                && result.PartialFailures is not null
                && result.PartialFailures.All(f =>
                    f is not null
                    && !string.IsNullOrWhiteSpace(f.Provider)
                    && !string.IsNullOrWhiteSpace(f.ErrorCode)
                    && f.ElapsedMs >= 0
                )
                && result.Offers is not null
                && result.Offers.All(SearchJourneyValidation.IsValidOffer)
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
        var json = JsonSerializer.Serialize(new CacheEnvelope(4, result), SerializerOptions);
        await db.StringSetAsync(key, json, ttl);
    }
}
