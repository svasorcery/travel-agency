using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class GenderTests
{
    [Theory]
    [InlineData("m")]
    [InlineData("M")]
    [InlineData("male")]
    [InlineData("MALE")]
    public void Parse_returns_male_for_m_or_male_variants(string input)
    {
        var r = Gender.Parse(input);
        r.IsError.ShouldBeFalse();
        r.Value.ShouldBe(Gender.Male);
    }

    [Theory]
    [InlineData("f")]
    [InlineData("F")]
    [InlineData("female")]
    [InlineData("FEMALE")]
    public void Parse_returns_female_for_f_or_female_variants(string input)
    {
        var r = Gender.Parse(input);
        r.IsError.ShouldBeFalse();
        r.Value.ShouldBe(Gender.Female);
    }

    [Theory]
    [InlineData("u")]
    [InlineData("U")]
    [InlineData("unspecified")]
    [InlineData("UNSPECIFIED")]
    public void Parse_returns_unspecified_for_u_or_unspecified_variants(string input)
    {
        var r = Gender.Parse(input);
        r.IsError.ShouldBeFalse();
        r.Value.ShouldBe(Gender.Unspecified);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("unknown")]
    [InlineData("")]
    public void Parse_returns_error_for_unknown_gender(string input)
    {
        var r = Gender.Parse(input);
        r.IsError.ShouldBeTrue();
    }

    [Fact]
    public void Male_is_singleton()
    {
        var m1 = Gender.Male;
        var m2 = Gender.Male;
        m1.ShouldBeSameAs(m2);
    }

    [Fact]
    public void Female_is_singleton()
    {
        var f1 = Gender.Female;
        var f2 = Gender.Female;
        f1.ShouldBeSameAs(f2);
    }

    [Fact]
    public void Unspecified_is_singleton()
    {
        var u1 = Gender.Unspecified;
        var u2 = Gender.Unspecified;
        u1.ShouldBeSameAs(u2);
    }
}
