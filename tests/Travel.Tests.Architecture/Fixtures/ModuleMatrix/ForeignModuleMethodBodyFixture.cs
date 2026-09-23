namespace Travel.Modules.Hotels.Core;

public static class ForeignModuleMethodBodyFixture
{
    public static object Call() => Travel.Modules.Flights.Core.ValueObjects.IataCode.Create("LED");
}
