namespace Travel.Modules.Flights.Core;

public interface LayerBypassFixture
{
    Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? ForeignLayer { get; }
}
