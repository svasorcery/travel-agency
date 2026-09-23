namespace Travel.Modules.Flights.Api.Endpoints;

public interface ApiContractBypassFixture
{
    Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested? ForeignContract { get; }
}
