using Shouldly;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class StayContractTests
{
    [Theory]
    [InlineData("+++++", false)]
    [InlineData("+012345", false)]
    [InlineData("+44 123", false)]
    [InlineData("44000000000", false)]
    [InlineData("+1234567890123456", false)]
    [InlineData("+1", true)]
    [InlineData("+44000000000", true)]
    public void Contact_phone_requires_structural_E164_without_inventing_a_country_dialplan(
        string phone,
        bool valid
    ) =>
        new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            phone,
            true,
            true
        )
            .IsValid(1)
            .ShouldBe(valid);

    [Fact]
    public void Nights_use_calendar_dates_across_daylight_saving()
    {
        var stay = StayDates.Create(new DateOnly(2027, 3, 27), new DateOnly(2027, 3, 29));
        stay.IsError.ShouldBeFalse();
        stay.Value.Nights.ShouldBe(2);
        StayDates
            .Create(new DateOnly(2027, 3, 29), new DateOnly(2027, 3, 29))
            .IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Unsupported_parties_are_refused(int adults) =>
        RoomOccupancy.Create(adults).IsError.ShouldBeTrue();

    [Fact]
    public void Money_is_exact_whole_stay_and_unknown_is_not_zero()
    {
        StayMoney.Create(120.25m, "GBP").Value.Amount.ShouldBe(120.25m);
        StayMoney.Create(-1m, "GBP").IsError.ShouldBeTrue();
        StayMoney.Create(1m, "gbp").IsError.ShouldBeTrue();
        typeof(IHotelSearchProvider)
            .GetMethods()
            .Select(m => m.Name)
            .ShouldNotContain("CreateAsync");
        typeof(PreparedStay).GetProperties().Select(p => p.Name).ShouldNotContain("QuoteId");
    }
}
