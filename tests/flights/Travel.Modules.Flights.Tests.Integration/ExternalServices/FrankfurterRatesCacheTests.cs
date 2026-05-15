using System.Globalization;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Cache;
using Travel.Modules.Flights.Infrastructure.ExternalServices;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.ExternalServices;

[Trait("Category", "Integration")]
public sealed class FrankfurterRatesCacheTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine").Build();
    private WireMockServer _wireMock = default!;
    private IConnectionMultiplexer _redis = default!;
    private FrankfurterClient _client = default!;
    private FrankfurterRatesCache _cache = default!;

    private static readonly CurrencyCode Usd = CurrencyCode.Create("USD").Value;
    private static readonly CurrencyCode Eur = CurrencyCode.Create("EUR").Value;

    public async ValueTask InitializeAsync()
    {
        await _redisContainer.StartAsync();

        _wireMock = WireMockServer.Start();

        var httpClient = new HttpClient { BaseAddress = new Uri(_wireMock.Url!) };
        _client = new FrankfurterClient(httpClient);

        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _cache = new FrankfurterRatesCache(_client, _redis);
    }

    public async ValueTask DisposeAsync()
    {
        _wireMock.Stop();
        _redis.Dispose();
        await _redisContainer.DisposeAsync();
    }

    private void StubFrankfurter(string from, string to, decimal rate)
    {
        _wireMock
            .Given(
                Request
                    .Create()
                    .WithPath("/latest")
                    .WithParam("base", from)
                    .WithParam("symbols", to)
                    .UsingGet()
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        FormattableString.Invariant(
                            $@"{{""amount"":1.0,""base"":""{from}"",""date"":""2026-01-01"",""rates"":{{""{to}"":{rate}}}}}"
                        )
                    )
            );
    }

    [Fact]
    public async Task GetRateAsync_same_currency_returns_one_without_http_call()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _cache.GetRateAsync(Usd, Usd, ct);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(1m);
        _wireMock.LogEntries.Count().ShouldBe(0);
    }

    [Fact]
    public async Task GetRateAsync_first_call_hits_http_second_call_hits_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        StubFrankfurter("USD", "EUR", 0.92m);

        // First call — should hit WireMock
        var first = await _cache.GetRateAsync(Usd, Eur, ct);
        first.IsError.ShouldBeFalse();
        first.Value.ShouldBe(0.92m);
        _wireMock.LogEntries.Count().ShouldBe(1);

        // Second call — should be served from Redis, no new HTTP request
        var second = await _cache.GetRateAsync(Usd, Eur, ct);
        second.IsError.ShouldBeFalse();
        second.Value.ShouldBe(0.92m);
        _wireMock.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task ConvertAsync_multiplies_amount_by_rate()
    {
        var ct = TestContext.Current.CancellationToken;
        StubFrankfurter("USD", "EUR", 0.9m);

        var amount = Money.Create(100m, Usd).Value;
        var result = await _cache.ConvertAsync(amount, Eur, ct);

        result.IsError.ShouldBeFalse();
        result.Value.Amount.ShouldBe(90m);
        result.Value.Currency.ShouldBe(Eur);
    }

    [Fact]
    public async Task Rate_round_trips_under_non_invariant_culture()
    {
        // Without InvariantCulture: decimal.ToString() under ru-RU writes "0,92" to Redis.
        // decimal.TryParse("0,92") under en-US (or InvariantCulture) returns false,
        // producing a cache miss and a fresh HTTP call that stores a NEW (possibly
        // culture-dependent) value — the round-trip is broken.
        //
        // Strategy: prime the cache while CurrentCulture = ru-RU (so broken code stores "0,92"),
        // then attempt to read while CurrentCulture = en-US (so broken code can't parse "0,92").
        // A fixed implementation always stores/reads with InvariantCulture, so it round-trips fine.
        var ct = TestContext.Current.CancellationToken;

        // Allow two Frankfurter calls: one for priming, potentially one for the broken read.
        StubFrankfurter("USD", "EUR", 0.92m);

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // 1. Prime cache under ru-RU
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var first = await _cache.GetRateAsync(Usd, Eur, ct);
            first.IsError.ShouldBeFalse();
            first.Value.ShouldBe(0.92m);

            // 2. Reset so next WireMock call counts separately
            var hitCountAfterPrime = _wireMock.LogEntries.Count();

            // 3. Read under en-US — broken code can't parse "0,92" → 0.92 would be lost
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var second = await _cache.GetRateAsync(Usd, Eur, ct);
            second.IsError.ShouldBeFalse();
            second.Value.ShouldBe(
                0.92m,
                "cache round-trip must survive culture change; InvariantCulture required"
            );

            // Fixed code makes exactly 1 HTTP call total (cache hit on second call)
            _wireMock.LogEntries.Count().ShouldBe(hitCountAfterPrime);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
