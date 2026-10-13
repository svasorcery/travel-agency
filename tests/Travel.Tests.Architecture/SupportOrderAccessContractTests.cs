using System.Reflection;
using Shouldly;
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
                    "FlightOrderOwnershipProof",
                    "FlightSupportJourneyState",
                    "FlightSupportTriageFacts",
                    "IFlightsSupportOrderAccess",
                    "IFlightsSupportTriageContext",
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
