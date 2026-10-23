namespace Travel.Modules.Trips.Application.Itineraries;

public interface TripsForeignInternalsFixture
{
    Flights.Core.ValueObjects.IataCode? Flight { get; }
    Hotels.Core.HotelPropertyReference? Hotel { get; }
    Rail.Core.StationRef? Station { get; }
}

public static class TripsForeignMethodBodyFixture
{
    public static object Call() => Flights.Core.ValueObjects.IataCode.Create("LED");
}
