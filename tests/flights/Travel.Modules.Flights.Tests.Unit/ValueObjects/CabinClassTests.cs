using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class CabinClassTests
{
    [Theory]
    [InlineData("economy", "economy")]
    [InlineData("ECONOMY", "economy")]
    [InlineData("Economy", "economy")]
    [InlineData("basic_economy", "economy")]
    [InlineData("BASIC_ECONOMY", "economy")]
    [InlineData("premium_economy", "premium_economy")]
    [InlineData("PREMIUM_ECONOMY", "premium_economy")]
    [InlineData("business", "business")]
    [InlineData("BUSINESS", "business")]
    [InlineData("first", "first")]
    [InlineData("FIRST", "first")]
    public void Parse_returns_correct_singleton_for_known_code(string input, string expectedCode)
    {
        var r = CabinClass.Parse(input);
        r.IsError.ShouldBeFalse();
        r.Value.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public void Parse_basic_economy_collapses_to_economy_singleton()
    {
        var r = CabinClass.Parse("basic_economy");
        r.IsError.ShouldBeFalse();
        r.Value.ShouldBe(CabinClass.Economy);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("charter")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_returns_validation_error_for_unknown_code(string input)
    {
        var r = CabinClass.Parse(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("CabinClass.Unknown");
    }

    [Fact]
    public void Parse_null_returns_validation_error()
    {
        var r = CabinClass.Parse(null!);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("CabinClass.Unknown");
    }

    [Fact]
    public void Singletons_have_correct_codes()
    {
        CabinClass.Economy.Code.ShouldBe("economy");
        CabinClass.PremiumEconomy.Code.ShouldBe("premium_economy");
        CabinClass.Business.Code.ShouldBe("business");
        CabinClass.First.Code.ShouldBe("first");
    }

    [Fact]
    public void ToString_returns_code()
    {
        CabinClass.Business.ToString().ShouldBe("business");
    }
}
