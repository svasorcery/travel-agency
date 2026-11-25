using Shouldly;
using Travel.Modules.Trips.Core;
using Xunit;
using static Travel.Modules.Trips.Tests.Unit.TripPlanTests;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripManualStayTests
{
    [Fact]
    public void Sorts_adjacent_stays_with_gaps_and_treats_reordering_as_a_noop()
    {
        var plan = Plan(6);
        var first = new TripStayInput("Дом", Start, Start.AddDays(1));
        var next = new TripStayInput("Апартаменты", Start.AddDays(1), Start.AddDays(2));
        var last = new TripStayInput("Отель", Start.AddDays(3), Start.AddDays(5));
        var saved = plan.Replace(
            plan.Title,
            plan.Brief.ToInput(),
            Days(plan),
            Now,
            [last, next, first]
        ).Value;
        saved.ManualStays.Select(stay => stay.ToInput()).ShouldBe([first, next, last]);
        saved
            .Replace(
                saved.Title,
                saved.Brief.ToInput(),
                Days(saved),
                Now.AddHours(1),
                [next, last, first]
            )
            .Value.ShouldBeSameAs(saved);

        var edited = next with { CheckOutDate = Start.AddDays(3) };
        saved
            .Replace(saved.Title, saved.Brief.ToInput(), Days(saved), Now, [first, edited, last])
            .Value.ManualStays[1]
            .ToInput()
            .ShouldBe(edited);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 3)]
    public void Rejects_a_shared_night_including_a_fully_nested_stay(int checkIn, int checkOut)
    {
        var plan = Plan(6);
        var first = new TripStayInput("Дом", Start, Start.AddDays(4));
        var next = new TripStayInput(
            "Апартаменты",
            Start.AddDays(checkIn),
            Start.AddDays(checkOut)
        );
        plan.Replace(plan.Title, plan.Brief.ToInput(), Days(plan), Now, [next, first])
            .FirstError.Code.ShouldBe("Trips.ManualStaysOverlap");
    }

    [Fact]
    public void Supports_up_to_thirteen_adjacent_stays_within_fourteen_calendar_days()
    {
        var plan = Plan(14);
        var thirteen = Enumerable
            .Range(0, 13)
            .Select(i => new TripStayInput("Дом", Start.AddDays(i), Start.AddDays(i + 1)))
            .ToArray();
        plan.Replace(plan.Title, plan.Brief.ToInput(), Days(plan), Now, thirteen)
            .Value.ManualStays.Count.ShouldBe(13);
        plan.Replace(plan.Title, plan.Brief.ToInput(), Days(plan), Now, [.. thirteen, thirteen[0]])
            .FirstError.ShouldBe(TripErrors.InvalidInput);
    }

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

        var updated = plan.Replace(plan.Title, plan.Brief.ToInput(), days, Now, [stay]).Value;

        updated.ManualStays.Single().ToInput().ShouldBe(stay);
        updated.Revision.ShouldBe(2);
        updated.Days[0].Blocks.Count.ShouldBe(8);
        updated.KnownUserEstimateSubtotal.ShouldBe("10.00");
        updated.UnknownEstimateCount.ShouldBe(0);
        updated
            .Replace(updated.Title, updated.Brief.ToInput(), days, Now.AddHours(1), [stay])
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
        plan.Replace(plan.Title, plan.Brief.ToInput(), Days(plan), Now, [stay])
            .FirstError.ShouldBe(TripErrors.InvalidInput);
    }

    [Fact]
    public void Guards_date_changes_and_accepts_an_explicit_adjustment_or_removal()
    {
        var empty = Plan(3);
        var stay = new TripStayInput("Гостевой дом", Start, Start.AddDays(2));
        var saved = empty
            .Replace(empty.Title, empty.Brief.ToInput(), Days(empty), Now, [stay])
            .Value;
        var shorter = saved.Brief.ToInput() with { EndDate = Start.AddDays(1) };
        var days = Days(saved)[..2];

        saved
            .Replace(saved.Title, shorter, days, Now)
            .FirstError.ShouldBe(TripErrors.DateRangeWouldExcludeStay);
        saved
            .Replace(
                saved.Title,
                shorter,
                days,
                Now,
                [stay with { CheckOutDate = shorter.EndDate }]
            )
            .Value.ManualStays.Single()
            .CheckOutDate.ShouldBe(shorter.EndDate);
        saved.Replace(saved.Title, shorter, days, Now, []).Value.ManualStays.ShouldBeEmpty();
        saved.Revision.ShouldBe(2);
        saved.ManualStays.Single().ToInput().ShouldBe(stay);
    }

    [Fact]
    public void Destination_change_requires_an_earlier_removal_but_currency_can_change()
    {
        var empty = Plan(3);
        var stay = new TripStayInput("Гостевой дом", Start, Start.AddDays(1));
        var next = new TripStayInput("Апартаменты", Start.AddDays(1), Start.AddDays(2));
        var saved = empty
            .Replace(empty.Title, empty.Brief.ToInput(), Days(empty), Now, [stay, next])
            .Value;
        var city = saved.Brief.ToInput() with { DestinationId = "kaliningrad" };
        var currency = saved.Brief.ToInput() with { Currency = "USD" };

        saved
            .Replace(saved.Title, city, Days(saved), Now, [])
            .FirstError.ShouldBe(TripErrors.DestinationChangeRequiresRemovingStay);
        saved
            .Replace(saved.Title, currency, Days(saved), Now)
            .Value.ManualStays.Select(item => item.ToInput())
            .ShouldBe([stay, next]);
        var cleared = saved.Replace(saved.Title, saved.Brief.ToInput(), Days(saved), Now, []).Value;
        cleared
            .Replace(cleared.Title, city, Days(cleared), Now)
            .Value.Brief.DestinationId.ShouldBe("kaliningrad");
    }
}
