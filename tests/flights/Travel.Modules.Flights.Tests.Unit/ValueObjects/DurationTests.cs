using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class DurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(191)]
    public void Journey_total_allows_positive_values_below_one_hundred_ninety_two_hours(int hours)
    {
        var result = Duration.CreateJourneyTotal(TimeSpan.FromHours(hours));
        result.IsError.ShouldBeFalse();
        result.Value.Value.ShouldBe(TimeSpan.FromHours(hours));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(192)]
    [InlineData(193)]
    public void Journey_total_rejects_nonpositive_and_upper_bound_values(int hours) =>
        Duration.CreateJourneyTotal(TimeSpan.FromHours(hours)).IsError.ShouldBeTrue();

    [Fact]
    public void Journey_total_retains_the_original_value_only_json_shape()
    {
        System
            .Text.Json.JsonSerializer.Serialize(
                Duration.CreateJourneyTotal(TimeSpan.FromHours(60)).Value
            )
            .ShouldBe("{\"Value\":\"2.12:00:00\"}");
    }

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
