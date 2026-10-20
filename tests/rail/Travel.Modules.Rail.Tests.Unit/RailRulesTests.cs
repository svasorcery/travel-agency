using Shouldly;
using Travel.Modules.Rail.Core;
using Xunit;

namespace Travel.Modules.Rail.Tests.Unit;

public sealed class RailRulesTests
{
    [Theory]
    [InlineData("s1")]
    [InlineData("s9600213")]
    public void Station_reference_retains_source_opaque_code(string code)
    {
        var reference = StationRef.Create("yandex-rasp", code);
        reference.IsError.ShouldBeFalse();
        reference.Value.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    public void Empty_or_control_ref_is_invalid(string code) =>
        StationRef.Create("yandex-rasp", code).IsError.ShouldBeTrue();

    [Theory]
    [InlineData("1234", "5", "RUB", "1234.05")]
    [InlineData("1234.0", "5.0", "USD", "1234.05")]
    [InlineData("0", "0", "EUR", "0.00")]
    [InlineData("9007199254740993", "5", "RUB", "9007199254740993.05")]
    public void Money_remains_exact(string whole, string cents, string currency, string expected) =>
        RailRules
            .Money(
                decimal.Parse(whole, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(cents, System.Globalization.CultureInfo.InvariantCulture),
                currency
            )
            .Value.ShouldBe(expected);

    [Theory]
    [InlineData("-1", "0", "RUB")]
    [InlineData("1234.5", "0", "RUB")]
    [InlineData("1", "100", "RUB")]
    [InlineData("1", "0.5", "RUB")]
    [InlineData("1", "0", "JPY")]
    [InlineData("79228162514264337593543950335", "99", "RUB")]
    public void Invalid_money_is_unavailable(string whole, string cents, string currency) =>
        RailRules
            .Money(
                decimal.Parse(whole, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(cents, System.Globalization.CultureInfo.InvariantCulture),
                currency
            )
            .IsError.ShouldBeTrue();

    [Fact]
    public void Source_reference_is_opaque_domain_value() =>
        StationRef.Create("another-source", "opaque:station/ref").IsError.ShouldBeFalse();
}
