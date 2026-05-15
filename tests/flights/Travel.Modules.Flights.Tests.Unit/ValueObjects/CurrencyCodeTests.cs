using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class CurrencyCodeTests
{
    [Theory]
    [InlineData("RUB")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void Create_returns_value_for_valid_input(string input)
    {
        var r = CurrencyCode.Create(input);
        r.IsError.ShouldBeFalse();
        r.Value.Value.ShouldBe(input);
    }

    [Theory]
    [InlineData("rub")]
    [InlineData("RU")]
    [InlineData("RUBS")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_returns_validation_error_for_invalid_input(string input)
    {
        var r = CurrencyCode.Create(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = CurrencyCode.Create("RUB").Value;
        var b = CurrencyCode.Create("RUB").Value;
        a.ShouldBe(b);
    }

    // ── Specific error codes per validation branch ────────────────────────────

    [Theory]
    [InlineData("", "CurrencyCode.Empty")]
    [InlineData("   ", "CurrencyCode.Empty")]
    public void Create_empty_input_returns_Empty_error_code(string input, string expectedCode)
    {
        var r = CurrencyCode.Create(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData("RU", "CurrencyCode.Length")]
    [InlineData("RUBS", "CurrencyCode.Length")]
    public void Create_wrong_length_returns_Length_error_code(string input, string expectedCode)
    {
        var r = CurrencyCode.Create(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData("rub", "CurrencyCode.Format")]
    [InlineData("RU1", "CurrencyCode.Format")]
    public void Create_non_uppercase_letters_returns_Format_error_code(
        string input,
        string expectedCode
    )
    {
        var r = CurrencyCode.Create(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe(expectedCode);
    }
}
