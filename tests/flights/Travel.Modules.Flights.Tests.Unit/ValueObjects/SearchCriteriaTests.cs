using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class SearchCriteriaTests
{
    private static IataCode Led => IataCode.Create("LED").Value;
    private static IataCode Jfk => IataCode.Create("JFK").Value;
    private static DateOnly Departure => new(2026, 8, 1);
    private static CabinClass Economy => CabinClass.Economy;
    private static CurrencyCode Usd => CurrencyCode.Create("USD").Value;

    [Fact]
    public void Create_one_way_returns_value_and_IsRoundTrip_is_false()
    {
        var r = SearchCriteria.Create(Led, Jfk, Departure, null, 1, Economy, Usd);

        r.IsError.ShouldBeFalse();
        r.Value.IsRoundTrip.ShouldBeFalse();
    }

    [Fact]
    public void Create_round_trip_returns_value_and_IsRoundTrip_is_true()
    {
        var returnDate = Departure.AddDays(7);

        var r = SearchCriteria.Create(Led, Jfk, Departure, returnDate, 1, Economy, Usd);

        r.IsError.ShouldBeFalse();
        r.Value.IsRoundTrip.ShouldBeTrue();
        r.Value.ReturnDate.ShouldBe(returnDate);
    }

    [Fact]
    public void Create_returns_error_when_return_is_before_departure()
    {
        var returnDate = Departure.AddDays(-1);

        var r = SearchCriteria.Create(Led, Jfk, Departure, returnDate, 1, Economy, Usd);

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("SearchCriteria.ReturnBeforeDeparture");
    }

    [Fact]
    public void Create_returns_error_when_origin_equals_destination()
    {
        var r = SearchCriteria.Create(Led, Led, Departure, null, 1, Economy, Usd);

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("SearchCriteria.SameOriginDestination");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Create_returns_error_for_invalid_passenger_count(int passengerCount)
    {
        var r = SearchCriteria.Create(Led, Jfk, Departure, null, passengerCount, Economy, Usd);

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("SearchCriteria.PassengerCount");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Create_returns_error_for_multi_pax_m1_constraint(int passengerCount)
    {
        var r = SearchCriteria.Create(Led, Jfk, Departure, null, passengerCount, Economy, Usd);

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("SearchCriteria.PassengerCount");
    }
}
