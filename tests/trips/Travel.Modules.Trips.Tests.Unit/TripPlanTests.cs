using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.DomainEvents;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripPlanTests
{
    internal static readonly DateOnly Start = new(2020, 1, 1);
    internal static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    internal static readonly OwnerIdentity Owner = OwnerIdentity
        .Create("https://identity.example.test/realms/travel", Guid.NewGuid())
        .Value;

    internal static TripBriefInput Brief(int days = 2) =>
        new(
            "saint-petersburg",
            "moscow",
            Start,
            Start.AddDays(days - 1),
            2,
            "RUB",
            "100",
            ["architecture", "food"],
            "relaxed"
        );

    internal static TripPlan Plan(int days = 2) =>
        TripPlan.Create(Guid.NewGuid(), Owner, "План", Brief(days), Now).Value;

    internal static TripDayInput[] Days(TripPlan plan) =>
        plan
            .Days.Select(day => new TripDayInput(
                day.Date,
                day.Blocks.Select(block => block.ToInput()).ToArray()
            ))
            .ToArray();

    internal static TripBlockInput Block(
        string? amount = null,
        string? startTime = null,
        string? endTime = null
    ) =>
        new(
            Guid.NewGuid(),
            "Прогулка",
            "Ручной план\nБез брони",
            startTime,
            endTime,
            amount is null ? null : new(amount, "RUB")
        );

    [Fact]
    public void Creates_revision_one_historical_empty_calendar_without_claims()
    {
        var plan = Plan(14);
        plan.Revision.ShouldBe(1);
        plan.Days.Select(day => day.Date).ShouldBe(Enumerable.Range(0, 14).Select(Start.AddDays));
        plan.Days.ShouldAllBe(day => day.Blocks.Count == 0);
        plan.KnownUserEstimateSubtotal.ShouldBe("0.00");
        plan.UnknownEstimateCount.ShouldBe(0);
        plan.Warnings.ShouldBeEmpty();
        plan.DomainEvents.Single().ShouldBeOfType<TripPlanCreated>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Rejects_invalid_day_range(int days) =>
        TripBrief.Create(Brief(days)).IsError.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Rejects_invalid_party(int adults) =>
        TripBrief.Create(Brief() with { Adults = adults }).IsError.ShouldBeTrue();

    [Fact]
    public void Rejects_unknown_city_enum_and_duplicate_interests()
    {
        TripBrief.Create(Brief() with { DestinationId = "unknown" }).IsError.ShouldBeTrue();
        TripBrief.Create(Brief() with { OriginId = "airport-id" }).IsError.ShouldBeTrue();
        TripBrief.Create(Brief() with { Currency = "GBP" }).IsError.ShouldBeTrue();
        TripBrief.Create(Brief() with { Pace = "fast" }).IsError.ShouldBeTrue();
        TripBrief.Create(Brief() with { Interests = ["food", "food"] }).IsError.ShouldBeTrue();
        TripBrief.Create(Brief() with { Interests = ["shopping"] }).IsError.ShouldBeTrue();
        CityCatalogue.Cities.Count.ShouldBe(3);
        CityCatalogue.Find("kaliningrad").Value.TimeZone.ShouldBe("Europe/Kaliningrad");
    }

    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("0.01", "0.01")]
    [InlineData("999999999.99", "999999999.99")]
    [InlineData("123", "123.00")]
    public void Money_is_exact_and_normalized(string input, string expected) =>
        PlanningMoney.Create(input, "RUB").Value.Amount.ShouldBe(expected);

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("1.0")]
    [InlineData("1.001")]
    [InlineData("1e2")]
    [InlineData("1000000000")]
    [InlineData("1,00")]
    [InlineData(" 1")]
    [InlineData("1.")]
    [InlineData(".00")]
    [InlineData("NaN")]
    public void Rejects_noncanonical_or_unbounded_money(string input) =>
        PlanningMoney.Create(input, "USD").IsError.ShouldBeTrue();

    [Fact]
    public void Uses_unicode_scalars_and_rejects_controls_and_broken_surrogates()
    {
        var hundred = string.Concat(Enumerable.Repeat("😀", 100));
        TripPlan.Create(Guid.NewGuid(), Owner, hundred, Brief(), Now).IsError.ShouldBeFalse();
        TripPlan.Create(Guid.NewGuid(), Owner, hundred + "😀", Brief(), Now).IsError.ShouldBeTrue();
        foreach (
            var title in new[] { "", " ", "bad\n", "bad\t", "bad\u007f", "bad\u0085", "\ud800" }
        )
            TripPlan.Create(Guid.NewGuid(), Owner, title, Brief(), Now).IsError.ShouldBeTrue();
        TripBlock
            .Create(Block() with { Note = new string('я', 500) }, "RUB")
            .IsError.ShouldBeFalse();
        TripBlock
            .Create(Block() with { Note = new string('я', 501) }, "RUB")
            .IsError.ShouldBeTrue();
        TripBlock.Create(Block() with { Note = "line\r\n" }, "RUB").IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData("10:00", null)]
    [InlineData(null, "11:00")]
    [InlineData("10:00", "10:00")]
    [InlineData("23:00", "01:00")]
    [InlineData("9:00", "10:00")]
    [InlineData("10:00:00", "11:00")]
    [InlineData("24:00", "25:00")]
    public void Rejects_invalid_local_time_pairs(string? start, string? end) =>
        TripBlock.Create(Block(startTime: start, endTime: end), "RUB").IsError.ShouldBeTrue();

    [Fact]
    public void Partial_estimates_and_overlaps_are_explicit()
    {
        var original = Plan(1);
        var saved = original
            .Replace(
                original.Title,
                original.Brief.ToInput(),
                [
                    new(
                        Start,
                        [Block("0.10", "10:00", "11:00"), Block("0.20", "10:30", "12:00"), Block()]
                    ),
                ],
                Now.AddMinutes(1)
            )
            .Value;
        saved.KnownUserEstimateSubtotal.ShouldBe("0.30");
        saved.UnknownEstimateCount.ShouldBe(1);
        saved.Warnings.ShouldBe(["OverlappingTimes"]);
        saved.Revision.ShouldBe(2);
        original.Days.Single().Blocks.ShouldBeEmpty();
    }

    [Fact]
    public void Maximum_112_estimates_sum_exactly_beyond_single_value_cap()
    {
        var plan = Plan(14);
        var days = plan
            .Days.Select(day => new TripDayInput(
                day.Date,
                Enumerable.Range(0, 8).Select(_ => Block("999999999.99")).ToArray()
            ))
            .ToArray();
        var saved = plan.Replace(plan.Title, plan.Brief.ToInput(), days, Now).Value;
        saved.KnownUserEstimateSubtotal.ShouldBe("111999999998.88");
        saved.UnknownEstimateCount.ShouldBe(0);
        saved.Warnings.ShouldBe(["KnownEstimateExceedsBudget"]);
    }

    [Fact]
    public void Rejects_wrong_currency_duplicate_identity_ninth_block_and_invalid_day_set()
    {
        var plan = Plan(1);
        var block = Block();
        plan.Replace(plan.Title, plan.Brief.ToInput(), [new(Start, [block, block])], Now)
            .IsError.ShouldBeTrue();
        plan.Replace(
                plan.Title,
                plan.Brief.ToInput(),
                [new(Start, Enumerable.Range(0, 9).Select(_ => Block()).ToArray())],
                Now
            )
            .IsError.ShouldBeTrue();
        plan.Replace(plan.Title, plan.Brief.ToInput(), [new(Start.AddDays(1), [])], Now)
            .IsError.ShouldBeTrue();
        plan.Replace(plan.Title, plan.Brief.ToInput(), [], Now).IsError.ShouldBeTrue();
        plan.Replace(
                plan.Title,
                plan.Brief.ToInput(),
                [new(Start, [Block("1") with { Estimate = new("1", "USD") }])],
                Now
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Move_reorder_edit_and_remove_preserve_identity_and_estimates()
    {
        var plan = Plan();
        var a = Block("1");
        var b = Block("2");
        var saved = plan.Replace(
            plan.Title,
            plan.Brief.ToInput(),
            [new(Start, [a, b]), new(Start.AddDays(1), [])],
            Now
        ).Value;
        var moved = saved
            .Replace(
                saved.Title,
                saved.Brief.ToInput(),
                [new(Start, [b with { Title = "Изменено" }]), new(Start.AddDays(1), [a])],
                Now
            )
            .Value;
        moved.Days[1].Blocks.Single().BlockId.ShouldBe(a.BlockId);
        moved.Days[1].Blocks.Single().Estimate!.Amount.ShouldBe("1.00");
        var removed = moved
            .Replace(
                moved.Title,
                moved.Brief.ToInput(),
                [new(Start, []), new(Start.AddDays(1), [a])],
                Now
            )
            .Value;
        removed.Days.SelectMany(day => day.Blocks).Count().ShouldBe(1);
        var reordered = saved
            .Replace(
                saved.Title,
                saved.Brief.ToInput(),
                [new(Start, [b, a]), new(Start.AddDays(1), [])],
                Now
            )
            .Value;
        reordered.Days[0].Blocks.Select(x => x.BlockId).ShouldBe([b.BlockId, a.BlockId]);
    }

    [Fact]
    public void Brief_changes_keep_blocks_and_reject_destructive_changes()
    {
        var original = Plan();
        var plan = original
            .Replace(
                original.Title,
                original.Brief.ToInput(),
                [new(Start, []), new(Start.AddDays(1), [Block()])],
                Now
            )
            .Value;
        plan.Replace(
                plan.Title,
                plan.Brief.ToInput() with
                {
                    EndDate = Start,
                },
                [new(Start, [])],
                Now
            )
            .FirstError.Code.ShouldBe("Trips.DateRangeWouldDropBlocks");
        plan.Replace(
                plan.Title,
                plan.Brief.ToInput() with
                {
                    DestinationId = "moscow",
                },
                Days(plan),
                Now
            )
            .FirstError.Code.ShouldBe("Trips.BriefChangeRequiresEmptyPlan");
        plan.Replace(plan.Title, plan.Brief.ToInput() with { Currency = "EUR" }, Days(plan), Now)
            .FirstError.Code.ShouldBe("Trips.BriefChangeRequiresEmptyPlan");
        var changed = plan.Replace(
            plan.Title,
            plan.Brief.ToInput() with
            {
                Adults = 9,
                Pace = "active",
                TotalBudget = null,
                Interests = [],
            },
            Days(plan),
            Now
        ).Value;
        changed.Days[1].Blocks.Single().BlockId.ShouldBe(plan.Days[1].Blocks.Single().BlockId);
        var extended = changed
            .Replace(
                changed.Title,
                changed.Brief.ToInput() with
                {
                    EndDate = Start.AddDays(2),
                },
                Days(changed).Append(new TripDayInput(Start.AddDays(2), [])).ToArray(),
                Now
            )
            .Value;
        extended.Days[2].Blocks.ShouldBeEmpty();
        extended.Days[1].Blocks.ShouldBe(changed.Days[1].Blocks);
    }

    [Fact]
    public void Date_extension_adds_empty_days_before_a_separate_block_edit()
    {
        var plan = Plan(1);
        plan.Replace(
                plan.Title,
                plan.Brief.ToInput() with
                {
                    EndDate = Start.AddDays(1),
                },
                [new(Start, []), new(Start.AddDays(1), [Block()])],
                Now
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void No_op_keeps_timestamp_and_metadata_at_max_revision_but_effective_change_fails()
    {
        var plan = Plan();
        plan.Replace(plan.Title, plan.Brief.ToInput(), Days(plan), Now.AddDays(1))
            .Value.ShouldBeSameAs(plan);
        var restored = TripPlan
            .Restore(
                plan.TripId,
                Owner,
                long.MaxValue,
                plan.Title,
                plan.Brief.ToInput(),
                plan.Destination with
                {
                    DisplayName = "Историческое название",
                },
                Days(plan),
                Now,
                Now
            )
            .Value;
        restored
            .Replace(restored.Title, restored.Brief.ToInput(), Days(restored), Now.AddMinutes(1))
            .Value.ShouldBeSameAs(restored);
        restored
            .Replace("Иное", restored.Brief.ToInput(), Days(restored), Now.AddMinutes(1))
            .FirstError.Code.ShouldBe("Trips.RevisionExhausted");
        restored.Destination.DisplayName.ShouldBe("Историческое название");
    }

    [Fact]
    public void Defensive_copies_keep_caller_arrays_from_mutating_saved_state()
    {
        var interests = new[] { "food" };
        var plan = TripPlan
            .Create(Guid.NewGuid(), Owner, "План", Brief(1) with { Interests = interests }, Now)
            .Value;
        interests[0] = "unknown";
        plan.Brief.Interests.ShouldBe(["food"]);
        var blocks = new[] { Block() };
        var updated = plan.Replace(
            plan.Title,
            plan.Brief.ToInput(),
            [new(Start, blocks)],
            Now
        ).Value;
        var id = updated.Days.Single().Blocks.Single().BlockId;
        blocks[0] = Block();
        updated.Days.Single().Blocks.Single().BlockId.ShouldBe(id);
    }
}
