using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class MultiLegCriteriaTests
{
    private static readonly DateOnly Date = new(2030, 6, 1);

    private static IataCode Airport(string code) => IataCode.Create(code).Value;

    private static RequestedFlightLeg Leg(string origin, string destination, int days = 0) =>
        RequestedFlightLeg.Create(Airport(origin), Airport(destination), Date.AddDays(days)).Value;

    private static ErrorOr<SearchCriteria> Create(IReadOnlyList<RequestedFlightLeg> legs) =>
        SearchCriteria.CreateMultiLeg(
            legs,
            2,
            CabinClass.Economy,
            CurrencyCode.Create("RUB").Value
        );

    [Fact]
    public void Two_independent_legs_preserve_exact_order_and_mode()
    {
        var first = Leg("LED", "DME");
        var second = Leg("VKO", "LED", 7);
        var result = Create([first, second]);
        result.IsError.ShouldBeFalse();
        result.Value.Legs.ShouldBe([first, second]);
        result.Value.RouteMode.ShouldBe(SearchRouteMode.ExplicitAirportLegs);
        result.Value.JourneyKind.ShouldBe(JourneyKind.MultiLeg);
        result.Value.IsRoundTrip.ShouldBeFalse();
        result.Value.ReturnDate.ShouldBeNull();
        result.Value.Origin.ShouldBe(first.Origin);
        result.Value.Destination.ShouldBe(first.Destination);
        result.Value.DepartureDate.ShouldBe(Date);
    }

    [Theory]
    [InlineData(1, JourneyKind.OneWay)]
    [InlineData(2, JourneyKind.RoundTrip)]
    [InlineData(3, JourneyKind.MultiLeg)]
    [InlineData(4, JourneyKind.MultiLeg)]
    public void Kind_depends_on_geometry_even_in_explicit_mode(int count, JourneyKind kind)
    {
        var legs = Enumerable
            .Range(0, count)
            .Select(i => i % 2 == 0 ? Leg("LED", "DME", i) : Leg("DME", "LED", i))
            .ToList();
        var result = Create(legs);
        result.IsError.ShouldBeFalse();
        result.Value.JourneyKind.ShouldBe(kind);
        result.Value.IsRoundTrip.ShouldBe(kind == JourneyKind.RoundTrip);
        result.Value.ReturnDate.ShouldBe(
            kind == JourneyKind.RoundTrip ? Date.AddDays(1) : (DateOnly?)null
        );
        result.Value.Legs.ShouldBe(legs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Rejects_unsupported_leg_counts(int count) =>
        Create(Enumerable.Repeat(Leg("LED", "DME"), count).ToArray()).IsError.ShouldBeTrue();

    [Fact]
    public void Rejects_null_leg_list_and_null_leg()
    {
        Create(null!).IsError.ShouldBeTrue();
        Create([null!]).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Requested_leg_rejects_equal_missing_or_malformed_endpoints_and_minimum_date()
    {
        RequestedFlightLeg.Create(Airport("LED"), Airport("LED"), Date).IsError.ShouldBeTrue();
        RequestedFlightLeg.Create(null!, Airport("LED"), Date).IsError.ShouldBeTrue();
        var malformed = System.Text.Json.JsonSerializer.Deserialize<IataCode>("{\"Value\":\"1\"}")!;
        RequestedFlightLeg.Create(malformed, Airport("LED"), Date).IsError.ShouldBeTrue();
        RequestedFlightLeg.Create(Airport("LED"), Airport("DME"), default).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Equal_local_dates_are_allowed_but_decreasing_dates_are_rejected()
    {
        Create([Leg("LED", "DME"), Leg("DME", "LED")]).IsError.ShouldBeFalse();
        Create([Leg("LED", "DME", 1), Leg("DME", "LED")]).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Criteria_copies_caller_list_and_exposes_readonly_collection()
    {
        var leg = Leg("LED", "DME");
        var source = new List<RequestedFlightLeg> { leg };
        var criteria = Create(source).Value;
        source[0] = Leg("VKO", "LED", 7);
        source.Clear();
        criteria.Legs.ShouldBe([leg]);
        Should.Throw<NotSupportedException>(() =>
            ((IList<RequestedFlightLeg>)criteria.Legs)[0] = Leg("DME", "LED")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_factory_adapts_into_ordered_location_legs(bool roundTrip)
    {
        var result = SearchCriteria.Create(
            Airport("LED"),
            Airport("DME"),
            Date,
            roundTrip ? Date.AddDays(7) : null,
            1,
            CabinClass.Economy,
            CurrencyCode.Create("RUB").Value
        );
        result.IsError.ShouldBeFalse();
        result.Value.RouteMode.ShouldBe(SearchRouteMode.LegacyLocations);
        result.Value.Legs.ShouldBe(
            roundTrip ? [Leg("LED", "DME"), Leg("DME", "LED", 7)] : [Leg("LED", "DME")]
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Explicit_criteria_keeps_existing_party_bounds(int count) =>
        SearchCriteria
            .CreateMultiLeg(
                [Leg("LED", "DME")],
                count,
                CabinClass.Economy,
                CurrencyCode.Create("RUB").Value
            )
            .IsError.ShouldBeTrue();
}
