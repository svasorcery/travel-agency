namespace Travel.Modules.Flights.Api.Endpoints;

public static class ApiContractMethodBodyBypassFixture
{
    public static object Create() =>
        new Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested("example", Guid.Empty);
}
