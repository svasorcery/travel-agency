using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class SliceTests
{
    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Svo = IataCode.Create("SVO").Value;
    private static readonly IataCode Jfk = IataCode.Create("JFK").Value;
    private static readonly IataCode Lax = IataCode.Create("LAX").Value;
    private static readonly DateTimeOffset Base = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static Segment MakeSegment(
        IataCode origin,
        IataCode dest,
        DateTimeOffset dep,
        DateTimeOffset arr
    ) => Segment.Create(origin, dest, dep, arr, "SU", "100", CabinClass.Economy).Value;

    [Fact]
    public void Create_returns_slice_for_single_segment()
    {
        var seg = MakeSegment(Led, Jfk, Base, Base.AddHours(9));
        var r = Slice.Create([seg]);
        r.IsError.ShouldBeFalse();
        r.Value.Origin.ShouldBe(Led);
        r.Value.Destination.ShouldBe(Jfk);
        r.Value.Segments.Count.ShouldBe(1);
        r.Value.DepartAt.ShouldBe(Base);
        r.Value.ArriveAt.ShouldBe(Base.AddHours(9));
    }

    [Fact]
    public void Create_returns_slice_for_connected_multi_segment()
    {
        // LED → SVO depart 10:00 arrive 11:30, SVO → JFK depart 13:00 arrive 22:00
        var seg1 = MakeSegment(Led, Svo, Base, Base.AddHours(1).AddMinutes(30));
        var seg2 = MakeSegment(Svo, Jfk, Base.AddHours(3), Base.AddHours(12));
        var r = Slice.Create([seg1, seg2]);
        r.IsError.ShouldBeFalse();
        r.Value.Origin.ShouldBe(Led);
        r.Value.Destination.ShouldBe(Jfk);
        r.Value.Segments.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_returns_error_for_empty_segment_list()
    {
        var r = Slice.Create([]);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
        r.FirstError.Code.ShouldBe("Slice.NoSegments");
    }

    [Fact]
    public void Create_returns_error_for_null_segment_list()
    {
        var r = Slice.Create(null!);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Slice.NoSegments");
    }

    [Fact]
    public void Create_returns_error_when_segments_are_discontinuous()
    {
        // Seg1: LED→SVO, Seg2: LAX→JFK — origin of seg2 doesn't match dest of seg1
        var seg1 = MakeSegment(Led, Svo, Base, Base.AddHours(2));
        var seg2 = MakeSegment(Lax, Jfk, Base.AddHours(3), Base.AddHours(12));
        var r = Slice.Create([seg1, seg2]);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Slice.Discontinuous");
    }

    [Fact]
    public void Create_returns_error_when_second_segment_departs_before_first_arrives()
    {
        // Seg1: LED→SVO depart 10:00 arrive 12:00, Seg2: SVO→JFK depart 11:00 — time inversion
        var seg1 = MakeSegment(Led, Svo, Base, Base.AddHours(2));
        var seg2 = MakeSegment(Svo, Jfk, Base.AddHours(1), Base.AddHours(10));
        var r = Slice.Create([seg1, seg2]);
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("Slice.TimeInversion");
    }

    [Fact]
    public void Duration_reflects_total_elapsed_time_from_first_departure_to_last_arrival()
    {
        var seg1 = MakeSegment(Led, Svo, Base, Base.AddHours(2));
        var seg2 = MakeSegment(Svo, Jfk, Base.AddHours(3), Base.AddHours(12));
        var r = Slice.Create([seg1, seg2]);
        r.Value.Duration.Value.ShouldBe(TimeSpan.FromHours(12));
    }
}
