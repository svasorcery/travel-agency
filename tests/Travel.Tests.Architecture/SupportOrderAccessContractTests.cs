using System.Reflection;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class SupportOrderAccessContractTests
{
    [Theory]
    [InlineData("Travel.Modules.Support.Core", "Travel.Modules.Support.Core", true)]
    [InlineData(
        "Travel.Modules.Support.Infrastructure",
        "Travel.Modules.Support.Infrastructure",
        true
    )]
    [InlineData("Travel.Modules.Support.Api", "Travel.Modules.Support.Api.Endpoints", true)]
    [InlineData("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Endpoints", true)]
    [InlineData("Travel.Host", "Travel.Host", true)]
    [InlineData(
        "Travel.Modules.Flights.Application",
        "Travel.Modules.Flights.Application.Support",
        false
    )]
    [InlineData("Travel.Modules.Support.Application", "Travel.Modules.Support.Application", false)]
    [InlineData("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Composition", false)]
    public void Published_Flights_contract_allows_only_its_real_consumers(
        string assembly,
        string name,
        bool forbidden
    )
    {
        var edge = new IlTypeEdge(
            assembly,
            name,
            name + ".Fixture",
            "Travel.IntegrationContracts.Flights",
            "Travel.IntegrationContracts.Flights",
            "Travel.IntegrationContracts.Flights.IFlightsSupportOrderAccess"
        );
        LayerArchitecturePolicy
            .FindContractConsumerIlViolations([edge])
            .Length.ShouldBe(forbidden ? 1 : 0);
    }

    [Fact]
    public void Published_Flights_leaf_has_only_BCL_and_expected_outcome_dependencies()
    {
        var leaf = typeof(Travel.IntegrationContracts.Flights.IFlightsSupportOrderAccess).Assembly;
        leaf.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ShouldAllBe(name =>
                name == "System.Runtime" || name == "System.Collections" || name == "ErrorOr"
            );
        leaf.GetExportedTypes()
            .Select(type => type.Name)
            .Order()
            .ToArray()
            .ShouldBe(
                new[]
                {
                    "FlightOrderBookingStatus",
                    "FlightOrderCandidate",
                    "FlightOrderCandidatePage",
                    "FlightOrderLeg",
                    "FlightOrderObservation",
                    "FlightOrderOwnershipProof",
                    "FlightSupportJourneyState",
                    "FlightSupportTriageFacts",
                    "IFlightsSupportOrderAccess",
                    "IFlightsSupportTriageContext",
                    "IFlightsTripsOrderAccess",
                }
            );
        typeof(Travel.IntegrationContracts.Flights.FlightOrderOwnershipProof)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order()
            .ToArray()
            .ShouldBe(new[] { "OrderId", "OwnerId", "StreamVersion", "VerifiedAt" });
        File.ReadAllText(
                Path.Combine(
                    Root(),
                    "shared/dotnet/Travel.IntegrationContracts.Flights/Travel.IntegrationContracts.Flights.csproj"
                )
            )
            .ShouldNotContain("ProjectReference");
    }

    [Theory]
    [InlineData("Trips.Core", "", true)]
    [InlineData("Trips.Infrastructure", "FlightLinks", true)]
    [InlineData("Trips.Api", "Endpoints", true)]
    [InlineData("Flights.Api", "Endpoints", true)]
    [InlineData("Flights.Api", "Composition.Unrelated", true)]
    [InlineData("Support.Application", "", true)]
    [InlineData("Trips.Application", "FlightLinks", false)]
    [InlineData("Flights.Application", "Trips", false)]
    [InlineData("Flights.Application", "Support", true)]
    [InlineData("Flights.Api", "Composition.FlightsModule", false)]
    [InlineData("Host", "", true)]
    public void Trips_facts_are_limited_to_producer_consumer_and_registration(
        string origin,
        string suffix,
        bool forbidden
    )
    {
        var assembly = origin == "Host" ? "Travel.Host" : "Travel.Modules." + origin;
        var type =
            assembly + "." + (suffix.EndsWith("FlightsModule") ? suffix : suffix + ".Fixture");
        var ns = type[..type.LastIndexOf('.')].TrimEnd('.');
        var target = typeof(IFlightsTripsOrderAccess);
        var edge = new IlTypeEdge(
            assembly,
            ns,
            type,
            target.Assembly.GetName().Name!,
            target.Namespace!,
            target.FullName!
        );
        LayerArchitecturePolicy
            .FindContractConsumerIlViolations([edge])
            .Length.ShouldBe(forbidden ? 1 : 0);
    }

    [Theory]
    [InlineData("IFlightsSupportOrderAccess")]
    [InlineData("FlightSupportTriageFacts")]
    public void Trips_consumer_cannot_use_existing_support_contracts(string targetName)
    {
        const string assembly = "Travel.Modules.Trips.Application";
        const string ns = assembly + ".FlightLinks";
        const string leaf = "Travel.IntegrationContracts.Flights";
        var edge = new IlTypeEdge(
            assembly,
            ns,
            ns + ".Fixture",
            leaf,
            leaf,
            leaf + "." + targetName
        );
        LayerArchitecturePolicy.FindContractConsumerIlViolations([edge]).Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("Travel.Modules.Trips.Core", true)]
    [InlineData("Travel.Modules.Trips.Infrastructure", true)]
    [InlineData("Travel.Modules.Trips.Api", true)]
    [InlineData("Travel.Modules.Flights.Core", true)]
    [InlineData("Travel.Modules.Flights.Infrastructure", true)]
    [InlineData("Travel.Modules.Trips.Application", false)]
    [InlineData("Travel.Modules.Flights.Application", false)]
    public void Project_reference_is_limited_to_the_required_layers(
        string project,
        bool forbidden
    ) =>
        LayerArchitecturePolicy
            .FindProjectReferenceViolations(
                Root(),
                project + ".csproj",
                ["Travel.IntegrationContracts.Flights.csproj"]
            )
            .Length.ShouldBe(forbidden ? 1 : 0);

    [Theory]
    [InlineData(typeof(FlightOrderCandidatePage), "Items NextOffset PageBoundReached")]
    [InlineData(typeof(FlightOrderCandidate), "BookingStatus OrderId PassengerCount Route")]
    [InlineData(typeof(FlightOrderLeg), "ArriveAt DepartAt Destination Origin")]
    [InlineData(
        typeof(FlightOrderObservation),
        "BookingStatus ObservedAt OrderId OwnerId PassengerCount ReviewRequired Route StreamVersion"
    )]
    public void Trips_exports_only_the_published_fields(Type shape, string properties) =>
        shape.GetProperties().Select(p => p.Name).Order().ShouldBe(properties.Split(' '));

    private static string Root()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
            if (File.Exists(Path.Combine(dir.FullName, "Travel.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException();
    }
}
