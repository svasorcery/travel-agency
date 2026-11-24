using Shouldly;
using Travel.Modules.Trips.Core;
using Xunit;
using static Travel.Modules.Trips.Tests.Unit.TripPlanTests;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripManualStayTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(13)]
    public void Saves_stay_at_calendar_boundaries_without_using_day_block_capacity(int nights)
    {
        var plan = Plan(nights + 1);
        var days = Days(plan);
        days[0] = days[0] with
        {
            Blocks = Enumerable.Range(0, 8).Select(_ => Block("1.25")).ToArray(),
        };
        var stay = new TripStayInput("Гостевой дом", Start, Start.AddDays(nights));

        var updated = plan.Replace(plan.Title, plan.Brief.ToInput(), days, Now, stay).Value;

        updated.ManualStay!.ToInput().ShouldBe(stay);
        updated.Revision.ShouldBe(2);
        updated.Days[0].Blocks.Count.ShouldBe(8);
        updated.KnownUserEstimateSubtotal.ShouldBe("10.00");
        updated.UnknownEstimateCount.ShouldBe(0);
        updated
            .Replace(updated.Title, updated.Brief.ToInput(), days, Now.AddHours(1), stay)
            .Value.ShouldBeSameAs(updated);
    }

    [Theory]
    [InlineData("", 0, 1, 2)]
    [InlineData("\uFEFF ", 0, 1, 2)]
    [InlineData("bad\nname", 0, 1, 2)]
    [InlineData("Гостевой дом", 0, 0, 1)]
    [InlineData("Гостевой дом", 1, 0, 2)]
    [InlineData("Гостевой дом", -1, 1, 2)]
    [InlineData("Гостевой дом", 0, 1, 1)]
    public void Rejects_invalid_stay(string place, int checkIn, int checkOut, int duration)
    {
        var plan = Plan(duration);
        var stay = new TripStayInput(place, Start.AddDays(checkIn), Start.AddDays(checkOut));
        plan.Replace(plan.Title, plan.Brief.ToInput(), Days(plan), Now, stay)
            .FirstError.ShouldBe(TripErrors.InvalidInput);
    }

    [Fact]
    public void Guards_date_changes_and_accepts_an_explicit_adjustment_or_removal()
    {
        var empty = Plan(3);
        var stay = new TripStayInput("Гостевой дом", Start, Start.AddDays(2));
        var saved = empty.Replace(empty.Title, empty.Brief.ToInput(), Days(empty), Now, stay).Value;
        var shorter = saved.Brief.ToInput() with { EndDate = Start.AddDays(1) };
        var days = Days(saved)[..2];

        saved
            .Replace(saved.Title, shorter, days, Now)
            .FirstError.ShouldBe(TripErrors.DateRangeWouldExcludeStay);
        saved
            .Replace(saved.Title, shorter, days, Now, stay with { CheckOutDate = shorter.EndDate })
            .Value.ManualStay!.CheckOutDate.ShouldBe(shorter.EndDate);
        saved.Replace(saved.Title, shorter, days, Now, null).Value.ManualStay.ShouldBeNull();
        saved.Revision.ShouldBe(2);
        saved.ManualStay!.ToInput().ShouldBe(stay);
    }

    [Fact]
    public void Destination_change_requires_an_earlier_removal_but_currency_can_change()
    {
        var empty = Plan();
        var stay = new TripStayInput("Гостевой дом", Start, Start.AddDays(1));
        var saved = empty.Replace(empty.Title, empty.Brief.ToInput(), Days(empty), Now, stay).Value;
        var city = saved.Brief.ToInput() with { DestinationId = "kaliningrad" };
        var currency = saved.Brief.ToInput() with { Currency = "USD" };

        saved
            .Replace(saved.Title, city, Days(saved), Now, null)
            .FirstError.ShouldBe(TripErrors.DestinationChangeRequiresRemovingStay);
        saved
            .Replace(saved.Title, currency, Days(saved), Now)
            .Value.ManualStay!.ToInput()
            .ShouldBe(stay);
        var cleared = saved
            .Replace(saved.Title, saved.Brief.ToInput(), Days(saved), Now, null)
            .Value;
        cleared
            .Replace(cleared.Title, city, Days(cleared), Now)
            .Value.Brief.DestinationId.ShouldBe("kaliningrad");
    }
}
