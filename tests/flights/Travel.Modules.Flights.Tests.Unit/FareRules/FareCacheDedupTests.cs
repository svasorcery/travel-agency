using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Cache;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class FareCacheDedupTests
{
    public class RedisProxy : DispatchProxy
    {
        public string? Payload;
        public object? Database;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "StringSetAsync")
            {
                Payload = args![1]!.ToString();
                return Task.FromResult(true);
            }
            return method.Name switch
            {
                "GetDatabase" => Database,
                "StringGetAsync" => Task.FromResult((RedisValue)Payload),
                _ => throw new InvalidOperationException("Unexpected Redis method: " + method.Name),
            };
        }
    }

    [Fact]
    public void Dedup_preserves_different_allowances_but_ignores_observation_time()
    {
        var dto = DuffelFareMappingTests.Offer([new("checked", 0)]);
        var first = DuffelOfferMapper
            .Map(dto, new FakeTimeProvider(DuffelFareMappingTests.Now))
            .Value;
        var later = DuffelOfferMapper
            .Map(dto, new FakeTimeProvider(DuffelFareMappingTests.Now.AddSeconds(1)))
            .Value;
        var different = DuffelOfferMapper
            .Map(
                DuffelFareMappingTests.Offer([new("checked", 1)]),
                new FakeTimeProvider(DuffelFareMappingTests.Now)
            )
            .Value with
        {
            FareConditions = first.FareConditions,
        };
        OfferDeduplicator
            .Dedup([
                new(first, first.TotalAmount, RankingPriceState.Native),
                new(later, later.TotalAmount, RankingPriceState.Native),
            ])
            .Count.ShouldBe(1);
        OfferDeduplicator
            .Dedup([
                new(first, first.TotalAmount, RankingPriceState.Native),
                new(different, different.TotalAmount, RankingPriceState.Native),
            ])
            .Count.ShouldBe(2);
    }

    [Fact]
    public async Task V5_roundtrip_retains_facts_and_rejects_v4_and_corrupt_evidence()
    {
        var offer = DuffelOfferMapper
            .Map(
                DuffelFareMappingTests.Offer([new("checked", 0)]),
                new FakeTimeProvider(DuffelFareMappingTests.Now)
            )
            .Value;
        var ranked = OfferRanker.Rank(
            [new(offer, offer.TotalAmount, RankingPriceState.Native)],
            offer.TotalAmount.Currency
        );
        var database = DispatchProxy.Create<IDatabase, RedisProxy>();
        var proxy = (RedisProxy)database;
        var redis = DispatchProxy.Create<IConnectionMultiplexer, RedisProxy>();
        ((RedisProxy)redis).Database = database;
        var cache = new SearchCacheRedis(redis, NullLogger<SearchCacheRedis>.Instance);
        await cache.SetAsync(
            "fictional",
            new SearchResult(ranked.Offers, [], ranked.Ranking, []),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken
        );
        var node = JsonNode.Parse(proxy.Payload!)!;
        node["schemaVersion"]!.GetValue<int>().ShouldBe(5);
        var hit = await cache.TryGetAsync("fictional", TestContext.Current.CancellationToken);
        hit.ShouldNotBeNull();
        var book = (Travel.Modules.Flights.Core.ValueObjects.Offer.BookableOffer)hit.Offers[0];
        book.FareRuleEvidence.ShouldNotBeNull().Passengers[0].CheckedBaggageQuantity.ShouldBe(0);
        node["schemaVersion"] = 4;
        proxy.Payload = node.ToJsonString();
        (
            await cache.TryGetAsync("fictional", TestContext.Current.CancellationToken)
        ).ShouldBeNull();
        node["schemaVersion"] = 5;
        node["result"]!["offers"]![0]!["fareRuleEvidence"]!["passengers"]![0]![
            "checkedBaggageQuantity"
        ] = -1;
        proxy.Payload = node.ToJsonString();
        (
            await cache.TryGetAsync("fictional", TestContext.Current.CancellationToken)
        ).ShouldBeNull();
    }
}
