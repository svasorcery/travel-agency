using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class ItineraryTests
{
    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Jfk = IataCode.Create("JFK").Value;
    private static readonly IataCode Svo = IataCode.Create("SVO").Value;
    private static readonly DateTimeOffset BaseOut = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BaseReturn = new(2026, 6, 15, 14, 0, 0, TimeSpan.Zero);

    private static Segment MakeSegment(
        IataCode o,
        IataCode d,
        DateTimeOffset dep,
        DateTimeOffset arr
    ) => Segment.Create(o, d, dep, arr, "SU", "100", CabinClass.Economy).Value;

    private static Slice MakeSlice(
        IataCode o,
        IataCode d,
        DateTimeOffset dep,
        DateTimeOffset arr
    ) => Slice.Create([MakeSegment(o, d, dep, arr)]).Value;

    [Fact]
    public void Create_one_way_itinerary_returns_success_with_IsOneWay_true()
    {
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var r = Itinerary.Create([outbound]);
        r.IsError.ShouldBeFalse();
        r.Value.IsOneWay.ShouldBeTrue();
        r.Value.IsRoundTrip.ShouldBeFalse();
        r.Value.Slices.Count.ShouldBe(1);
    }

    [Fact]
    public void Create_round_trip_itinerary_returns_success_with_IsRoundTrip_true()
    {
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var ret = MakeSlice(Jfk, Led, BaseReturn, BaseReturn.AddHours(9));
        var r = Itinerary.Create([outbound, ret]);
        r.IsError.ShouldBeFalse();
        r.Value.IsRoundTrip.ShouldBeTrue();
        r.Value.IsOneWay.ShouldBeFalse();
        r.Value.Slices.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_returns_error_for_empty_slice_list()
    {
        var r = Itinerary.Create([]);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("Itinerary.NoSlices");
    }

    [Fact]
    public void Create_returns_error_for_null_slice_list()
    {
        var r = Itinerary.Create(null!);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Itinerary.NoSlices");
    }

    [Fact]
    public void Create_returns_error_for_three_slices()
    {
        var s1 = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var s2 = MakeSlice(Jfk, Svo, BaseReturn, BaseReturn.AddHours(9));
        // third slice: SVO → LED, use a different base time to avoid duration > 48h
        var baseThird = BaseReturn.AddDays(1);
        var s3 = MakeSlice(Svo, Led, baseThird, baseThird.AddHours(2));
        var r = Itinerary.Create([s1, s2, s3]);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Itinerary.TooManySlices");
    }

    [Fact]
    public void TotalDuration_sums_all_slice_durations()
    {
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var ret = MakeSlice(Jfk, Led, BaseReturn, BaseReturn.AddHours(9));
        var r = Itinerary.Create([outbound, ret]);
        r.Value.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(18));
    }
}
