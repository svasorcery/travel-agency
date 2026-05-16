using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class FareConditionsTests
{
    [Fact]
    public void Equality_is_structural_when_all_properties_match()
    {
        var a = new FareConditions(true, false, "ABC123", "Economy");
        var b = new FareConditions(true, false, "ABC123", "Economy");
        a.ShouldBe(b);
    }

    [Fact]
    public void Equality_differs_when_ChangeAllowed_differs()
    {
        var a = new FareConditions(true, false, "ABC", null);
        var b = new FareConditions(false, false, "ABC", null);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_differs_when_RefundAllowed_differs()
    {
        var a = new FareConditions(true, true, null, null);
        var b = new FareConditions(true, false, null, null);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_differs_when_FareBasisCode_differs()
    {
        var a = new FareConditions(true, true, "Y", null);
        var b = new FareConditions(true, true, "Q", null);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_is_structural_with_null_optional_fields()
    {
        var a = new FareConditions(false, false, null, null);
        var b = new FareConditions(false, false, null, null);
        a.ShouldBe(b);
    }

    [Fact]
    public void Properties_are_set_correctly()
    {
        var fc = new FareConditions(true, true, "Y26", "Business");
        fc.ChangeAllowed.ShouldBeTrue();
        fc.RefundAllowed.ShouldBeTrue();
        fc.FareBasisCode.ShouldBe("Y26");
        fc.CabinClassMarketing.ShouldBe("Business");
    }
}
