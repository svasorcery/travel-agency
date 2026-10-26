namespace Travel.Modules.Trips.Core.Itineraries
{
    public interface TripsCoreInfrastructureFixture
    {
        Infrastructure.Itineraries.CuratedItineraryFacts Source { get; }
    }
}

namespace Travel.Modules.Trips.Application.Itineraries
{
    public interface TripsApplicationInfrastructureFixture
    {
        Infrastructure.Itineraries.CuratedItineraryFacts Source { get; }
    }

    public interface TripsApplicationAiContractFixture
    {
        Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested Request { get; }
    }
}

namespace Travel.Modules.Trips.Api.Endpoints
{
    public interface TripsEndpointInfrastructureFixture
    {
        Infrastructure.Itineraries.CuratedItineraryFacts Source { get; }
    }

    public interface TripsApiAiContractFixture
    {
        Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested Request { get; }
    }

    public static class TripsAiMethodBodyFixture
    {
        public static object Create() =>
            new Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested("example", Guid.Empty);
    }
}

namespace Travel.Modules.Trips.Api.Contracts
{
    public interface TripsContractInfrastructureFixture
    {
        Infrastructure.Itineraries.CuratedItineraryFacts Source { get; }
    }
}

namespace Travel.Modules.Trips.Api.CompositionExtra
{
    public interface TripsCompositionExtraInfrastructureFixture
    {
        Infrastructure.Itineraries.CuratedItineraryFacts Source { get; }
    }
}

namespace Travel.Modules.Trips.Api.Composition
{
    public interface TripsCompositionInfrastructureFixture
    {
        Infrastructure.Itineraries.CuratedItineraryFacts Source { get; }
    }
}
