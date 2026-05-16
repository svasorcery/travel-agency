using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class SegmentTests
{
    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Jfk = IataCode.Create("JFK").Value;
    private static readonly DateTimeOffset Base = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly CabinClass Economy = CabinClass.Economy;

    [Fact]
    public void Create_returns_segment_for_valid_inputs()
    {
        var dep = Base;
        var arr = Base.AddHours(9);
        var r = Segment.Create(Led, Jfk, dep, arr, "SU", "100", Economy);
        r.IsError.ShouldBeFalse();
        r.Value.Origin.ShouldBe(Led);
        r.Value.Destination.ShouldBe(Jfk);
        r.Value.DepartAt.ShouldBe(dep);
        r.Value.ArriveAt.ShouldBe(arr);
        r.Value.CarrierCode.ShouldBe("SU");
        r.Value.FlightNumber.ShouldBe("100");
        r.Value.Cabin.ShouldBe(Economy);
    }

    [Fact]
    public void Duration_is_difference_between_arrival_and_departure()
    {
        var dep = Base;
        var arr = Base.AddHours(9);
        var r = Segment.Create(Led, Jfk, dep, arr, "SU", "100", Economy);
        r.Value.Duration.ShouldBe(TimeSpan.FromHours(9));
    }

    [Fact]
    public void Create_returns_error_when_arrival_equals_departure()
    {
        var r = Segment.Create(Led, Jfk, Base, Base, "SU", "100", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("Segment.NonPositiveDuration");
    }

    [Fact]
    public void Create_returns_error_when_arrival_is_before_departure()
    {
        var r = Segment.Create(Led, Jfk, Base, Base.AddHours(-1), "SU", "100", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Segment.NonPositiveDuration");
    }

    [Fact]
    public void Create_returns_error_when_origin_equals_destination()
    {
        var r = Segment.Create(Led, Led, Base, Base.AddHours(1), "SU", "100", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Segment.SameOriginDestination");
    }

    [Fact]
    public void Create_returns_error_when_carrier_code_is_empty()
    {
        var r = Segment.Create(Led, Jfk, Base, Base.AddHours(9), "", "100", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Segment.CarrierEmpty");
    }

    [Fact]
    public void Create_returns_error_when_carrier_code_is_whitespace()
    {
        var r = Segment.Create(Led, Jfk, Base, Base.AddHours(9), "   ", "100", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Segment.CarrierEmpty");
    }

    [Fact]
    public void Create_returns_error_when_flight_number_is_empty()
    {
        var r = Segment.Create(Led, Jfk, Base, Base.AddHours(9), "SU", "", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Segment.FlightNumberEmpty");
    }

    [Fact]
    public void Create_returns_error_when_flight_number_is_whitespace()
    {
        var r = Segment.Create(Led, Jfk, Base, Base.AddHours(9), "SU", "  ", Economy);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Segment.FlightNumberEmpty");
    }
}
