using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class DurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(47)]
    public void Create_returns_value_for_valid_hours(int hours)
    {
        var ts = TimeSpan.FromHours(hours);
        var r = Duration.Create(ts);
        r.IsError.ShouldBeFalse();
        r.Value.Value.ShouldBe(ts);
    }

    [Fact]
    public void Create_returns_error_for_zero_duration()
    {
        var r = Duration.Create(TimeSpan.Zero);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("Duration.NonPositive");
    }

    [Fact]
    public void Create_returns_error_for_negative_duration()
    {
        var r = Duration.Create(TimeSpan.FromMinutes(-30));
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("Duration.NonPositive");
    }

    [Fact]
    public void Create_returns_error_for_duration_at_least_48_hours()
    {
        var r = Duration.Create(TimeSpan.FromDays(3));
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("Duration.TooLong");
    }

    [Fact]
    public void Equality_is_structural()
    {
        var ts = TimeSpan.FromHours(5);
        var a = Duration.Create(ts).Value;
        var b = Duration.Create(ts).Value;
        a.ShouldBe(b);
    }
}
