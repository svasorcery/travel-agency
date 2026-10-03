using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Infrastructure.Cache;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Search;

public sealed class SearchCachePartyEnvelopeTests
{
    public class RedisProxy : DispatchProxy
    {
        public string? Payload;
        public object? Database;

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name switch
            {
                "GetDatabase" => Database,
                "StringGetAsync" => Task.FromResult((RedisValue)Payload),
                "StringSetAsync" => Task.FromResult(true),
                _ => throw new InvalidOperationException("Unexpected Redis method: " + method.Name),
            };
    }

    private static string Envelope(bool unknown = false)
    {
        var dto = JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Providers",
                    "Duffel",
                    "Fixtures",
                    "offer-oneway.json"
                )
            )
        )!["data"]!["offers"]![0]!.Deserialize<DuffelOfferDto>()!;
        if (unknown)
            dto = dto with
            {
                PaymentRequirements = new(null),
                PassengerIdentityDocumentsRequired = null,
            };
        var offer = DuffelOfferMapper.Map(dto, new FakeTimeProvider()).Value;
        var ranked = OfferRanker.Rank(
            [new(offer, offer.TotalAmount, RankingPriceState.Native)],
            offer.TotalAmount.Currency
        );
        var result = new SearchResult(ranked.Offers, [], ranked.Ranking, []);
        return JsonSerializer.Serialize(
            new { schemaVersion = 3, result },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() },
            }
        );
    }

    private static async Task<SearchResult?> Read(string json)
    {
        var db = DispatchProxy.Create<IDatabase, RedisProxy>();
        ((RedisProxy)db).Payload = json;
        var redis = DispatchProxy.Create<IConnectionMultiplexer, RedisProxy>();
        ((RedisProxy)redis).Database = db;
        return await new SearchCacheRedis(redis, NullLogger<SearchCacheRedis>.Instance).TryGetAsync(
            "fictional",
            TestContext.Current.CancellationToken
        );
    }

    [Theory]
    [InlineData("supportsHold")]
    [InlineData("requiresIdentityDocuments")]
    public async Task Missing_capability_properties_are_misses_even_in_new_envelope(string field)
    {
        var node = JsonNode.Parse(Envelope())!;
        node["result"]!["offers"]![0]!["party"]!.AsObject().Remove(field);
        (await Read(node.ToJsonString())).ShouldBeNull();
    }

    [Theory]
    [InlineData("old-version")]
    [InlineData("missing-skips")]
    [InlineData("missing-party")]
    public async Task Old_or_incomplete_envelope_is_miss(string corruption)
    {
        var node = JsonNode.Parse(Envelope())!;
        if (corruption == "old-version")
            node["schemaVersion"] = 2;
        if (corruption == "missing-skips")
            node["result"]!.AsObject().Remove("skippedProviders");
        if (corruption == "missing-party")
            node["result"]!["offers"]![0]!.AsObject().Remove("party");
        (await Read(node.ToJsonString())).ShouldBeNull();
    }

    [Fact]
    public async Task Explicit_unknown_capabilities_are_truthful_cache_hit()
    {
        (await Read(Envelope(true))).ShouldNotBeNull();
    }
}
