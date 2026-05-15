using System.Globalization;
using ErrorOr;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.ExternalServices;

namespace Travel.Modules.Flights.Infrastructure.Cache;

public sealed class FrankfurterRatesCache(FrankfurterClient client, IConnectionMultiplexer redis)
    : IFxRates
{
    private static string CacheKey(CurrencyCode from, CurrencyCode to) =>
        $"flights:fx:{from.Value}:{to.Value}";

    public async Task<ErrorOr<decimal>> GetRateAsync(
        CurrencyCode from,
        CurrencyCode to,
        CancellationToken ct
    )
    {
        if (from == to)
            return 1m;

        var db = redis.GetDatabase();
        var key = CacheKey(from, to);

        var cached = await db.StringGetAsync(key);
        if (
            cached.HasValue
            && decimal.TryParse(
                cached.ToString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var cachedRate
            )
        )
            return cachedRate;

        var result = await client.GetRateAsync(from, to, ct);
        if (result.IsError)
            return result;

        var rate = result.Value;
        await db.StringSetAsync(
            key,
            rate.ToString(CultureInfo.InvariantCulture),
            TimeSpan.FromHours(24)
        );

        return rate;
    }

    public async Task<ErrorOr<Money>> ConvertAsync(
        Money amount,
        CurrencyCode to,
        CancellationToken ct
    )
    {
        var rateResult = await GetRateAsync(amount.Currency, to, ct);
        if (rateResult.IsError)
            return rateResult.FirstError;

        return Money.Create(amount.Amount * rateResult.Value, to);
    }
}
