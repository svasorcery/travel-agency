using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class IataCodeTests
{
    [Theory]
    [InlineData("LED")]
    [InlineData("DME")]
    [InlineData("JFK")]
    public void Create_returns_value_for_valid_input(string input)
    {
        var r = IataCode.Create(input);
        r.IsError.ShouldBeFalse();
        r.Value.Value.ShouldBe(input);
    }

    [Theory]
    [InlineData("led")]
    [InlineData("LE")]
    [InlineData("LEDX")]
    [InlineData("LE1")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_returns_validation_error_for_invalid_input(string input)
    {
        var r = IataCode.Create(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = IataCode.Create("LED").Value;
        var b = IataCode.Create("LED").Value;
        a.ShouldBe(b);
    }
}
