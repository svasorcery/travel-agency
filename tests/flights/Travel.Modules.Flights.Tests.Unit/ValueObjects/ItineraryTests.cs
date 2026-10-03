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

    [Fact]
    public void Fresh_factory_rejects_null_slice_without_throwing()
    {
        Itinerary.Create([null!]).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Five_slices_exceed_the_supported_limit()
    {
        var slices = Enumerable
            .Range(0, 5)
            .Select(i => MakeSlice(Led, Jfk, BaseOut.AddDays(i), BaseOut.AddDays(i).AddHours(9)))
            .ToArray();
        var result = Itinerary.Create(slices);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Itinerary.TooManySlices");
    }

    [Fact]
    public void Next_leg_may_depart_at_exact_previous_arrival_instant_with_another_offset()
    {
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var departure = outbound.ArriveAt.ToOffset(TimeSpan.FromHours(-4));
        var inbound = MakeSlice(Jfk, Led, departure, departure.AddHours(9));
        var result = Itinerary.Create([outbound, inbound]);
        result.IsError.ShouldBeFalse();
        result.Value.JourneyKind.ShouldBe(JourneyKind.RoundTrip);
        result.Value.Slices[1].DepartAt.Offset.ShouldBe(TimeSpan.FromHours(-4));
    }

    [Theory]
    [InlineData("Origin", "{\"Value\":\"SVO\"}")]
    [InlineData("Duration", "{\"Value\":\"02:00:00\"}")]
    [InlineData("Segments", "[]")]
    [InlineData("Segments", "[null]")]
    [InlineData("Duration", "null")]
    public void Fresh_factory_rejects_malformed_deserialized_slice(string property, string value)
    {
        var slice = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(slice)
        )!;
        node[property] = System.Text.Json.Nodes.JsonNode.Parse(value);
        var malformed = System.Text.Json.JsonSerializer.Deserialize<Slice>(node.ToJsonString())!;
        Itinerary.Create([malformed]).IsError.ShouldBeTrue();
    }

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
    public void Create_accepts_three_independent_slices()
    {
        var s1 = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var s2 = MakeSlice(Jfk, Svo, BaseReturn, BaseReturn.AddHours(9));
        // third slice: SVO → LED, use a different base time to avoid duration > 48h
        var baseThird = BaseReturn.AddDays(1);
        var s3 = MakeSlice(Svo, Led, baseThird, baseThird.AddHours(2));
        var r = Itinerary.Create([s1, s2, s3]);
        r.IsError.ShouldBeFalse();
        r.Value.IsRoundTrip.ShouldBeFalse();
    }

    [Fact]
    public void TotalDuration_sums_all_slice_durations()
    {
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var ret = MakeSlice(Jfk, Led, BaseReturn, BaseReturn.AddHours(9));
        var r = Itinerary.Create([outbound, ret]);
        r.Value.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(18));
    }

    [Fact]
    public void Create_accepts_open_jaw_without_classifying_it_as_round_trip()
    {
        // outbound: LED→JFK, inbound: SVO→LED — inbound origin (SVO) ≠ outbound destination (JFK)
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var wrongReturn = MakeSlice(Svo, Led, BaseReturn, BaseReturn.AddHours(9));
        var r = Itinerary.Create([outbound, wrongReturn]);
        r.IsError.ShouldBeFalse();
        r.Value.IsRoundTrip.ShouldBeFalse();
    }

    [Fact]
    public void Four_fifteen_hour_slices_sum_to_sixty_hours_excluding_ground_gaps()
    {
        var slices = Enumerable
            .Range(0, 4)
            .Select(i =>
                MakeSlice(Led, Jfk, BaseOut.AddDays(i * 2), BaseOut.AddDays(i * 2).AddHours(15))
            )
            .ToList();
        var result = Itinerary.Create(slices);
        result.IsError.ShouldBeFalse();
        result.Value.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(60));
    }

    [Fact]
    public void Later_local_clock_can_still_invert_utc_departure()
    {
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var next = MakeSlice(
            Jfk,
            Led,
            new DateTimeOffset(2026, 6, 1, 20, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 6, 2, 4, 0, 0, TimeSpan.FromHours(3))
        );
        Itinerary.Create([outbound, next]).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Fresh_itinerary_copies_both_slice_and_original_segment_lists()
    {
        var original = MakeSegment(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var segments = new List<Segment> { original };
        var slice = Slice.Create(segments).Value;
        var slices = new List<Slice> { slice };
        var itinerary = Itinerary.Create(slices).Value;
        segments[0] = MakeSegment(Svo, Led, BaseReturn, BaseReturn.AddHours(1));
        slices.Clear();
        slice.Segments.ShouldBe([original]);
        slice.DepartAt.ShouldBe(BaseOut);
        slice.Duration.Value.ShouldBe(TimeSpan.FromHours(9));
        itinerary.Slices.Count.ShouldBe(1);
        itinerary.Slices[0].Segments.ShouldBe([original]);
        itinerary.Slices[0].DepartAt.ShouldBe(BaseOut);
        itinerary.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(9));
        Should.Throw<NotSupportedException>(() => ((IList<Slice>)itinerary.Slices).Clear());
        Should.Throw<NotSupportedException>(() =>
            ((IList<Segment>)itinerary.Slices[0].Segments).Clear()
        );
    }

    [Fact]
    public void Create_accepts_proper_round_trip_with_matching_endpoints()
    {
        // outbound: LED→JFK, inbound: JFK→LED — correct continuity
        var outbound = MakeSlice(Led, Jfk, BaseOut, BaseOut.AddHours(9));
        var ret = MakeSlice(Jfk, Led, BaseReturn, BaseReturn.AddHours(9));
        var r = Itinerary.Create([outbound, ret]);
        r.IsError.ShouldBeFalse();
        r.Value.IsRoundTrip.ShouldBeTrue();
    }
}
