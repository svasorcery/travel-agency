namespace Travel.Modules.Hotels.Application;

// An open placeholder may be ignored; its actual foreign constraint and argument must not be.
public sealed class ForeignGenericConstraintsFixture<T>
    where T : Travel.Modules.Flights.Core.Providers.IFlightSearchProvider
{
    public static void Method<U>()
        where U : Travel.Modules.Flights.Core.Providers.IFlightBookingProvider { }

    public List<Travel.Modules.Flights.Core.ValueObjects.IataCode> Values { get; } = [];
}
