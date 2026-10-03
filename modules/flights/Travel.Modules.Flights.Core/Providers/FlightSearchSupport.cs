namespace Travel.Modules.Flights.Core.Providers;

public sealed record FlightSearchSupport(bool Supported, string? ReasonCode = null)
{
    public static FlightSearchSupport Available { get; } = new(true);
    public static FlightSearchSupport PassengerCountUnsupported { get; } =
        new(false, "passenger-count-unsupported");
}
