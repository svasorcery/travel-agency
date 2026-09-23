namespace Travel.Modules.Hotels.Application;

public interface ForeignModuleInterfaceFixture
{
    Travel.Modules.Flights.Core.ValueObjects.IataCode? ForeignCode { get; }
}
