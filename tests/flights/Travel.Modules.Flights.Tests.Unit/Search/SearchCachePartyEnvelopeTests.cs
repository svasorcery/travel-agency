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
            new { schemaVersion = 4, result },
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

    [Fact]
    public async Task Old_version_three_is_a_miss_even_with_current_offer_fields()
    {
        var node = JsonNode.Parse(Envelope())!;
        node["schemaVersion"] = 3;
        (await Read(node.ToJsonString())).ShouldBeNull();
    }

    [Theory]
    [InlineData("totalDuration")]
    [InlineData("sliceDuration")]
    [InlineData("nullSegments")]
    [InlineData("unknownOfferKind")]
    [InlineData("unknownSkip")]
    [InlineData("partyDate")]
    public async Task Malformed_fresh_route_or_unknown_discriminator_is_a_miss(string corruption)
    {
        var node = JsonNode.Parse(Envelope())!;
        var offer = node["result"]!["offers"]![0]!;
        var itinerary = offer["itinerary"]!;
        switch (corruption)
        {
            case "totalDuration":
                itinerary["totalDuration"]!["value"] = "01:00:00";
                break;
            case "sliceDuration":
                itinerary["slices"]![0]!["duration"]!["value"] = "01:00:00";
                break;
            case "nullSegments":
                itinerary["slices"]![0]!["segments"] = null;
                break;
            case "unknownOfferKind":
                offer["$type"] = "unknown";
                break;
            case "unknownSkip":
                node["result"]!["skippedProviders"] = JsonNode.Parse(
                    "[{\"provider\":\"travelpayouts\",\"reasonCode\":\"unknown\"}]"
                );
                break;
            case "partyDate":
                offer["party"]!["firstDepartureLocalDate"] = "2027-01-02";
                break;
        }
        (await Read(node.ToJsonString())).ShouldBeNull();
    }

    [Fact]
    public async Task Journey_unsupported_skip_survives_v4_envelope()
    {
        var node = JsonNode.Parse(Envelope())!;
        node["result"]!["skippedProviders"] = JsonNode.Parse(
            "[{\"provider\":\"travelpayouts\",\"reasonCode\":\"journey-unsupported\"}]"
        );
        (await Read(node.ToJsonString())).ShouldNotBeNull();
    }

    [Fact]
    public async Task V4_write_read_roundtrip_preserves_full_route_failures_skips_ranking_and_offsets()
    {
        var criteria = MultiLegSearchHandlerTests.Criteria();
        var offer = MultiLegSearchHandlerTests.Offer(criteria, hours: 15);
        var rank = OfferRanker.Rank(
            [new(offer, offer.TotalAmount, RankingPriceState.Native)],
            criteria.Currency
        );
        var result = new SearchResult(
            rank.Offers,
            [new("additional", "ProviderFailure", 7)],
            rank.Ranking,
            [new("travelpayouts", "journey-unsupported")]
        );
        var db = DispatchProxy.Create<IDatabase, RedisProxy>();
        var redis = DispatchProxy.Create<IConnectionMultiplexer, RedisProxy>();
        ((RedisProxy)redis).Database = db;
        var cache = new SearchCacheRedis(redis, NullLogger<SearchCacheRedis>.Instance);
        await cache.SetAsync(
            "fictional",
            result,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken
        );
        JsonNode.Parse(((RedisProxy)db).Payload!)!["schemaVersion"]!.GetValue<int>().ShouldBe(4);
        var restored = await cache.TryGetAsync("fictional", TestContext.Current.CancellationToken);
        restored.ShouldNotBeNull();
        JsonSerializer.Serialize(restored).ShouldBe(JsonSerializer.Serialize(result));
        restored.Offers.Single().Itinerary.Slices.Count.ShouldBe(4);
        restored
            .Offers.Single()
            .Itinerary.Slices[0]
            .DepartAt.Offset.ShouldBe(TimeSpan.FromHours(3));
    }
}
