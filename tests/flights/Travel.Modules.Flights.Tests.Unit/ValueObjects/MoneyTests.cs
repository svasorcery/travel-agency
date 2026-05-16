using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class MoneyTests
{
    [Fact]
    public void Create_returns_value_for_positive_amount()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var r = Money.Create(100m, currency);

        r.IsError.ShouldBeFalse();
        r.Value.Amount.ShouldBe(100m);
        r.Value.Currency.ShouldBe(currency);
    }

    [Fact]
    public void Create_returns_value_for_zero_amount()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var r = Money.Create(0m, currency);

        r.IsError.ShouldBeFalse();
        r.Value.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Create_returns_validation_error_for_negative_amount()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var r = Money.Create(-1m, currency);

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Money.Negative");
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Add_returns_sum_for_same_currency()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var a = Money.Create(100m, currency).Value;
        var b = Money.Create(50m, currency).Value;

        var r = a.Add(b);

        r.IsError.ShouldBeFalse();
        r.Value.Amount.ShouldBe(150m);
        r.Value.Currency.ShouldBe(currency);
    }

    [Fact]
    public void Add_returns_validation_error_for_different_currencies()
    {
        var rub = CurrencyCode.Create("RUB").Value;
        var usd = CurrencyCode.Create("USD").Value;
        var a = Money.Create(100m, rub).Value;
        var b = Money.Create(50m, usd).Value;

        var r = a.Add(b);

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Money.CurrencyMismatch");
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Equality_is_structural()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var a = Money.Create(100m, currency).Value;
        var b = Money.Create(100m, currency).Value;

        a.ShouldBe(b);
    }

    [Fact]
    public void Equality_differs_on_amount()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var a = Money.Create(100m, currency).Value;
        var b = Money.Create(99m, currency).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_differs_on_currency()
    {
        var rub = CurrencyCode.Create("RUB").Value;
        var usd = CurrencyCode.Create("USD").Value;
        var a = Money.Create(100m, rub).Value;
        var b = Money.Create(100m, usd).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ToString_formats_correctly()
    {
        var currency = CurrencyCode.Create("RUB").Value;
        var money = Money.Create(100.50m, currency).Value;

        money.ToString().ShouldBe("100.50 RUB");
    }

    // ── Additional negative branches ──────────────────────────────────────────

    [Fact]
    public void Add_with_very_large_amounts_does_not_overflow_for_reasonable_values()
    {
        // decimal can hold up to 79,228,162,514,264,337,593,543,950,335 — flight prices
        // are always in the millions at most, so addition should succeed without overflow.
        var currency = CurrencyCode.Create("USD").Value;
        var a = Money.Create(9_999_999m, currency).Value;
        var b = Money.Create(9_999_999m, currency).Value;
        var r = a.Add(b);
        r.IsError.ShouldBeFalse();
        r.Value.Amount.ShouldBe(19_999_998m);
    }

    [Fact]
    public void ToString_uses_invariant_culture_regardless_of_thread_culture()
    {
        // In some locales (e.g. ru-RU) the decimal separator is a comma.
        // Money.ToString() must always use '.' as the decimal separator.
        var savedCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("ru-RU");
            var currency = CurrencyCode.Create("RUB").Value;
            var money = Money.Create(1234.56m, currency).Value;
            money.ToString().ShouldBe("1,234.56 RUB");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = savedCulture;
        }
    }
}
