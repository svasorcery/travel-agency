namespace Travel.Modules.Trips.Core.BriefExtraction
{
    public interface TripsCoreBriefContractFixture
    {
        Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefRequested Request { get; }
    }
}

namespace Travel.Modules.Trips.Application.Itineraries
{
    public interface TripsItineraryBriefContractFixture
    {
        Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefParsed Reply { get; }
    }
}

namespace Travel.Modules.Trips.Api.Contracts
{
    public interface TripsUnrelatedBriefContractFixture
    {
        Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefFields Fields { get; }
    }
}
