using Shouldly;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Modules.Trips.Tests.Unit;

public class TripBriefDateNormalizerTests
{
    private static readonly DateTimeOffset Anchor = DateTimeOffset.Parse("2026-10-27T21:30:00Z");

    [Theory]
    [InlineData("с 2028-02-28 на три дня", "2028-02-28", "2028-03-01")]
    [InlineData("с 31 декабря 2026 года на три дня", "2026-12-31", "2027-01-02")]
    [InlineData("с 2026-01-01 по 2026-01-01", "2026-01-01", "2026-01-01")]
    [InlineData("завтра на один день", "2026-10-29", "2026-10-29")]
    public void Normalizes_only_supported_dates(string text, string start, string end)
    {
        var result = TripBriefDateNormalizer.Normalize(text, Anchor, "Europe/Moscow");
        result.StartDate.Value!.Value.GetString().ShouldBe(start);
        result.EndDate.Value!.Value.GetString().ShouldBe(end);
    }

    [Fact]
    public void Relative_anchor_obeys_selected_zone_and_midnight_expiry()
    {
        var result = TripBriefDateNormalizer.Normalize(
            "завтра на 2 дня",
            Anchor,
            "Europe/Kaliningrad"
        );
        result.StartDate.Value!.Value.GetString().ShouldBe("2026-10-28");
        TripBriefDateNormalizer
            .ExpiresAt(Anchor, "Europe/Moscow", true)
            .ShouldBe(Anchor.AddMinutes(10));
        var near = DateTimeOffset.Parse("2026-10-27T20:59:00Z");
        TripBriefDateNormalizer
            .ExpiresAt(near, "Europe/Moscow", true)
            .ShouldBe(DateTimeOffset.Parse("2026-10-27T21:00:00Z"));
    }

    [Theory]
    [InlineData("с 2026-11-03 по 2026-11-01", "invalid", "reversedDates")]
    [InlineData("с 2026-11-01 по 2026-11-05 на 3 дня", "ambiguous", "dateDurationConflict")]
    [InlineData("с 1 мая по 3 мая", "ambiguous", "yearMissing")]
    [InlineData("в пятницу 2026-11-01 на 3 дня", "ambiguous", "weekdayDateConflict")]
    public void Conflicts_block_whole_date_group(string text, string state, string issue)
    {
        var result = TripBriefDateNormalizer.Normalize(text, Anchor, "Europe/Moscow");
        result.StartDate.State.ShouldBe(state);
        result.EndDate.State.ShouldBe(state);
        result.Issues.Select(x => x.Code).ShouldContain(issue);
    }

    [Fact]
    public void End_and_duration_never_infer_start()
    {
        var result = TripBriefDateNormalizer.Normalize("до 2026-11-03 на 3 дня", Anchor, null);
        result.StartDate.State.ShouldBe("missing");
        result.EndDate.Value!.Value.GetString().ShouldBe("2026-11-03");
    }

    [Theory]
    [InlineData("с 2026-02-30 на 3 дня", "invalid", "missing", "invalidDate")]
    [InlineData("с 9999-12-31 на 2 дня", "invalid", "invalid", "invalidDate")]
    [InlineData("с 2026-11-01 по 2026-12-01", "invalid", "invalid", "dateBounds")]
    [InlineData(
        "с 2026-11-01 по 2026-11-02 по 2026-11-03",
        "ambiguous",
        "ambiguous",
        "dateConflict"
    )]
    [InlineData("с 03/04/2027 по 05/04/2027", "ambiguous", "ambiguous", "dateFormatAmbiguous")]
    public void Invalid_calendar_and_explicit_conflicts_never_partly_pass(
        string text,
        string start,
        string end,
        string issue
    )
    {
        var result = TripBriefDateNormalizer.Normalize(text, Anchor, "Europe/Moscow");
        result.StartDate.State.ShouldBe(start);
        result.EndDate.State.ShouldBe(end);
        result.Issues.Select(x => x.Code).ShouldContain(issue);
    }

    [Fact]
    public void Null_zone_has_no_relative_guess_and_does_not_borrow_end()
    {
        var result = TripBriefDateNormalizer.Normalize("завтра на три дня", Anchor, null);
        result.StartDate.State.ShouldBe("ambiguous");
        result.EndDate.State.ShouldBe("ambiguous");
        result.DurationDays.Value!.Value.GetInt32().ShouldBe(3);
        TripBriefDateNormalizer.ExpiresAt(Anchor, null, true).ShouldBe(Anchor.AddMinutes(10));
    }

    [Fact]
    public void Duration_without_start_and_nights_have_distinct_outcomes()
    {
        TripBriefDateNormalizer
            .Normalize("на три дня", Anchor, "Europe/Moscow")
            .StartDate.State.ShouldBe("missing");
        var nights = TripBriefDateNormalizer.Normalize("на три ночи", Anchor, "Europe/Moscow");
        nights.DurationDays.State.ShouldBe("unsupported");
        nights.StartDate.State.ShouldBe("missing");
        TripBriefDateNormalizer
            .Normalize("на 15 дней", Anchor, "Europe/Moscow")
            .DurationDays.State.ShouldBe("invalid");
    }
}
