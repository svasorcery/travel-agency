using Shouldly;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class HotelsTripsReservationContractTests
{
    private const string ContractAssembly = "Travel.IntegrationContracts.Hotels";
    private const string ContractNamespace = ContractAssembly + ".Trips";

    [Theory]
    [InlineData("Travel.Modules.Hotels.Application", ".Trips", ".Fixture", false)]
    [InlineData("Travel.Modules.Hotels.Application", "", ".Fixture", true)]
    [InlineData("Travel.Modules.Hotels.Api", ".Composition", ".HotelsModule", false)]
    [InlineData("Travel.Modules.Hotels.Api", ".Composition", ".UnrelatedModule", true)]
    [InlineData("Travel.Modules.Hotels.Api", ".Endpoints", ".Fixture", true)]
    [InlineData("Travel.Modules.Trips.Application", ".HotelLinks", ".Fixture", false)]
    [InlineData("Travel.Modules.Trips.Application", ".FlightLinks", ".Fixture", true)]
    [InlineData("Travel.Modules.Trips.Core", "", ".Fixture", true)]
    [InlineData("Travel.Modules.Trips.Infrastructure", "", ".Fixture", true)]
    [InlineData("Travel.Modules.Trips.Api", ".Composition", ".TripsModule", false)]
    [InlineData("Travel.Modules.Trips.Api", ".Composition", ".UnrelatedModule", true)]
    [InlineData("Travel.Modules.Trips.Api", ".Endpoints", ".Fixture", true)]
    [InlineData("Travel.Host", "", ".Fixture", true)]
    [InlineData("Travel.Shared.Web", "", ".Fixture", true)]
    public void Published_Hotels_contract_allows_only_producer_consumer_and_registration(
        string assembly,
        string namespaceSuffix,
        string typeSuffix,
        bool forbidden
    )
    {
        var originNamespace = assembly + namespaceSuffix;
        var edge = new IlTypeEdge(
            assembly,
            originNamespace,
            originNamespace + typeSuffix,
            ContractAssembly,
            ContractNamespace,
            ContractNamespace + ".IHotelsTripsReservationAccess"
        );

        LayerArchitecturePolicy
            .FindContractConsumerIlViolations([edge])
            .Length.ShouldBe(forbidden ? 1 : 0);
    }

    [Theory]
    [InlineData("Hotels", "Application", false)]
    [InlineData("Hotels", "Api", false)]
    [InlineData("Hotels", "Core", true)]
    [InlineData("Hotels", "Infrastructure", true)]
    [InlineData("Trips", "Application", false)]
    [InlineData("Trips", "Core", true)]
    [InlineData("Trips", "Infrastructure", true)]
    [InlineData("Trips", "Api", false)]
    [InlineData("Flights", "Application", true)]
    public void Published_Hotels_project_reference_allows_only_required_layers(
        string module,
        string layer,
        bool forbidden
    ) =>
        LayerArchitecturePolicy
            .FindProjectReferenceViolations(
                "",
                $"Travel.Modules.{module}.{layer}.csproj",
                [ContractAssembly + ".csproj"]
            )
            .Length.ShouldBe(forbidden ? 1 : 0);

    [Fact]
    public void Published_Hotels_leaf_exposes_only_the_minimal_observation()
    {
        var leaf = typeof(IHotelsTripsReservationAccess).Assembly;
        leaf.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ShouldAllBe(name =>
                name == "System.Runtime" || name == "System.Collections" || name == "ErrorOr"
            );
        leaf.GetExportedTypes()
            .Select(type => type.Name)
            .Order()
            .ShouldBe([
                nameof(HotelTripReservationObservation),
                nameof(HotelTripReservationState),
                nameof(IHotelsTripsReservationAccess),
            ]);
        typeof(HotelTripReservationObservation)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ShouldBe(["CheckIn", "CheckOut", "PropertyName", "State"]);
    }
}
