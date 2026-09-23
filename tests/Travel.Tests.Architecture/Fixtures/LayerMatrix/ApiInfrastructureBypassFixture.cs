namespace Travel.Modules.Flights.Api.CompositionExtra;

public interface ApiInfrastructureBypassFixture
{
    Travel.Modules.Flights.Infrastructure.Persistence.FlightsDbContext? ForeignInfrastructure { get; }
}
