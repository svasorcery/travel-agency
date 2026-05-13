using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class DateRangeTests
{
    [Fact]
    public void Create_returns_value_for_same_from_and_to()
    {
        var date = new DateOnly(2026, 6, 1);
        var r = DateRange.Create(date, date);
        r.IsError.ShouldBeFalse();
        r.Value.From.ShouldBe(date);
        r.Value.To.ShouldBe(date);
    }

    [Fact]
    public void Create_returns_value_when_to_is_after_from()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 10);
        var r = DateRange.Create(from, to);
        r.IsError.ShouldBeFalse();
        r.Value.From.ShouldBe(from);
        r.Value.To.ShouldBe(to);
    }

    [Fact]
    public void Create_returns_validation_error_when_to_is_before_from()
    {
        var from = new DateOnly(2026, 6, 10);
        var to = new DateOnly(2026, 6, 1);
        var r = DateRange.Create(from, to);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("DateRange.Inverted");
    }

    [Fact]
    public void LengthInDays_returns_zero_for_same_day_range()
    {
        var date = new DateOnly(2026, 6, 1);
        var r = DateRange.Create(date, date);
        r.Value.LengthInDays.ShouldBe(0);
    }

    [Fact]
    public void LengthInDays_returns_correct_count()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 11);
        var r = DateRange.Create(from, to);
        r.Value.LengthInDays.ShouldBe(10);
    }

    [Fact]
    public void Equality_is_structural()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 10);
        var a = DateRange.Create(from, to).Value;
        var b = DateRange.Create(from, to).Value;
        a.ShouldBe(b);
    }
}
