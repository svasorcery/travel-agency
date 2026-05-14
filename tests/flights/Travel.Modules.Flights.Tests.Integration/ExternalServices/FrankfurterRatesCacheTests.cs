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
}
