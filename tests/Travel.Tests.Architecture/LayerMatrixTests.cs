using ArchUnitNET.Loader;
using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class LayerMatrixTests
{
    [Fact]
    public void Unresolved_generic_dependency_types_use_namespace_fallback_without_crashing()
    {
        var origins = ArchitectureTestBase
            .Architecture.Types.Where(t =>
                t.FullName.StartsWith("Travel.Modules.Hotels.Core", StringComparison.Ordinal)
                && t.Dependencies.Any(d => d.Target.Assembly is null)
            )
            .ToArray();
        origins.ShouldNotBeEmpty();
        foreach (var origin in origins)
        foreach (var dependency in origin.Dependencies.Where(d => d.Target.Assembly is null))
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Unresolved ArchUnit dependency: {origin.FullName} -> {dependency.Target.FullName}; namespace={dependency.Target.Namespace?.FullName}"
            );
        LayerArchitecturePolicy.FindLayerTypeViolations(origins).ShouldBeEmpty();
        LayerArchitecturePolicy.FindContractConsumerTypeViolations(origins).ShouldBeEmpty();
    }

    private static readonly string Root = FindRoot();

    [Theory]
    [InlineData("Core", "Application")]
    [InlineData("Core", "Infrastructure")]
    [InlineData("Core", "Api")]
    [InlineData("Application", "Infrastructure")]
    [InlineData("Application", "Api")]
    [InlineData("Infrastructure", "Api")]
    public void All_six_forbidden_layer_directions_are_explicit(string source, string target)
    {
        LayerArchitecturePolicy.IsForbiddenLayerEdge(source, target).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Debug", false)]
    [InlineData("Release", true)]
    public async Task Imported_unused_layer_edge_is_detected_in_effective_configuration(
        string configuration,
        bool expectedViolation
    )
    {
        var fixture = Path.Combine(
            Root,
            "tests",
            "Travel.Tests.Architecture",
            "Fixtures",
            "LayerMatrix",
            "Source.proj"
        );
        var references = await EvaluatedProjectReferences.ForProjectAsync(fixture, configuration);
        var source = Path.Combine(
            Root,
            "modules",
            "flights",
            "Travel.Modules.Flights.Core",
            "Travel.Modules.Flights.Core.csproj"
        );
        var violations = LayerArchitecturePolicy.FindProjectReferenceViolations(
            Root,
            source,
            references
        );

        if (expectedViolation)
            violations.ShouldContain(violation =>
                violation.Contains("Flights.Core", StringComparison.Ordinal)
                && violation.Contains("Flights.Application", StringComparison.Ordinal)
            );
        else
            violations.ShouldBeEmpty();
    }

    [Fact]
    public void Controlled_type_dependencies_cross_layer_and_Api_boundaries()
    {
        var model = new ArchLoader()
            .LoadAssemblies(
                typeof(Travel.Modules.Flights.Core.LayerBypassFixture).Assembly,
                typeof(Travel.Modules.Flights.Application.Queries.SearchFlightsQuery).Assembly,
                typeof(Travel.Modules.Flights.Infrastructure.Persistence.FlightsDbContext).Assembly,
                typeof(Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested).Assembly
            )
            .Build();
        var fixtureTypes = model
            .Types.Where(type =>
                type.FullName.EndsWith("LayerBypassFixture", StringComparison.Ordinal)
                || type.FullName.EndsWith(
                    "ApiInfrastructureBypassFixture",
                    StringComparison.Ordinal
                )
                || type.FullName.EndsWith("ApiContractBypassFixture", StringComparison.Ordinal)
            )
            .ToArray();

        LayerArchitecturePolicy
            .FindLayerTypeViolations(fixtureTypes)
            .ShouldContain(violation =>
                violation.Contains("LayerBypassFixture", StringComparison.Ordinal)
            );
        LayerArchitecturePolicy
            .FindApiInfrastructureTypeViolations(fixtureTypes)
            .ShouldContain(violation =>
                violation.Contains("ApiInfrastructureBypassFixture", StringComparison.Ordinal)
            );
        LayerArchitecturePolicy
            .FindContractConsumerTypeViolations(fixtureTypes)
            .ShouldContain(violation =>
                violation.Contains("ApiContractBypassFixture", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void Trips_contract_selector_cannot_disappear()
    {
        var types = ArchitectureTestBase.Architecture.Types.ToArray();
        LayerArchitecturePolicy.FindImplementedApiSelectorViolations(types).ShouldBeEmpty();
        var selected = types
            .Where(type =>
                type.Assembly.Name == "Travel.Modules.Trips.Api"
                && (
                    type.Namespace?.FullName == "Travel.Modules.Trips.Api.Contracts"
                    || type.Namespace?.FullName.StartsWith(
                        "Travel.Modules.Trips.Api.Contracts.",
                        StringComparison.Ordinal
                    ) == true
                )
            )
            .ToArray();
        selected.ShouldNotBeEmpty();
        LayerArchitecturePolicy
            .FindImplementedApiSelectorViolations(types.Except(selected))
            .ShouldBe([
                "implemented Api selector is empty: Travel.Modules.Trips.Api / Travel.Modules.Trips.Api.Contracts",
            ]);
    }

    [Theory]
    [InlineData("Travel.Modules.Trips.Core.Itineraries.TripsCoreInfrastructureFixture", "Core")]
    [InlineData(
        "Travel.Modules.Trips.Application.Itineraries.TripsApplicationInfrastructureFixture",
        "Application"
    )]
    public void Trips_itinerary_layers_reject_own_infrastructure(string originName, string layer)
    {
        var origin = TripsFixtureArchitecture().Types.Single(type => type.FullName == originName);
        LayerArchitecturePolicy
            .FindLayerTypeViolations([origin])
            .ShouldBe([
                $"{originName} -> Travel.Modules.Trips.Infrastructure.Itineraries.CuratedMoscowFacts (Trips.{layer} -> Trips.Infrastructure)",
            ]);
    }

    [Theory]
    [InlineData("Travel.Modules.Trips.Api.Endpoints.TripsEndpointInfrastructureFixture", true)]
    [InlineData("Travel.Modules.Trips.Api.Contracts.TripsContractInfrastructureFixture", true)]
    [InlineData(
        "Travel.Modules.Trips.Api.CompositionExtra.TripsCompositionExtraInfrastructureFixture",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Api.Composition.TripsCompositionInfrastructureFixture",
        false
    )]
    public void Trips_Api_infrastructure_is_allowed_only_in_Composition(
        string originName,
        bool forbidden
    )
    {
        var origin = TripsFixtureArchitecture().Types.Single(type => type.FullName == originName);
        origin.Dependencies.ShouldContain(dependency =>
            dependency.Target.FullName
            == "Travel.Modules.Trips.Infrastructure.Itineraries.CuratedMoscowFacts"
        );
        var violations = LayerArchitecturePolicy.FindApiInfrastructureTypeViolations([origin]);
        if (forbidden)
            violations.ShouldBe([
                $"{originName} -> Travel.Modules.Trips.Infrastructure.Itineraries.CuratedMoscowFacts",
            ]);
        else
            violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Travel.Modules.Trips.Application.Itineraries.TripsApplicationAiContractFixture")]
    [InlineData("Travel.Modules.Trips.Api.Endpoints.TripsApiAiContractFixture")]
    public void Trips_has_no_AI_contract_consumer(string originName)
    {
        var origin = TripsFixtureArchitecture().Types.Single(type => type.FullName == originName);
        LayerArchitecturePolicy
            .FindContractConsumerTypeViolations([origin])
            .ShouldBe([
                $"{originName} -> Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested",
            ]);
    }

    [Fact]
    public void Curated_Moscow_source_retains_its_direct_TestOnly_marker()
    {
        // Marker proof is structural; actual Production omission/fail-closed proof belongs to
        // TripsItineraryCompositionTests, including factory descriptors with no ImplementationType.
        var productionTypes = ArchitectureTestBase.Architecture.Types;
        foreach (
            var expected in new[]
            {
                typeof(Travel.Modules.Trips.Core.Itineraries.ItineraryPolicy),
                typeof(Travel.Modules.Trips.Application.Itineraries.ItineraryCandidateService),
                typeof(Travel.Modules.Trips.Infrastructure.Itineraries.CuratedMoscowFacts),
                typeof(Travel.Modules.Trips.Api.Contracts.ItineraryCandidateResponseEncoder),
                typeof(Travel.Modules.Trips.Api.Endpoints.ItineraryCandidateEndpoint),
                typeof(Travel.Modules.Trips.Api.Endpoints.ItineraryAvailabilityEndpoint),
            }
        )
            productionTypes.ShouldContain(type =>
                type.FullName == expected.FullName
                && type.Assembly.Name == expected.Assembly.GetName().Name
            );
        typeof(Travel.Modules.Trips.Infrastructure.Itineraries.CuratedMoscowFacts)
            .IsDefined(typeof(Travel.Shared.Abstractions.TestOnlyAttribute), inherit: false)
            .ShouldBeTrue();
    }

    private static ArchUnitNET.Domain.Architecture TripsFixtureArchitecture() =>
        new ArchLoader()
            .LoadAssemblies(
                typeof(Travel.Modules.Trips.Core.Itineraries.TripsCoreInfrastructureFixture).Assembly,
                typeof(Travel.Modules.Trips.Infrastructure.Itineraries.CuratedMoscowFacts).Assembly,
                typeof(Travel.IntegrationContracts.AI.NlSearch.NlSearchRequested).Assembly
            )
            .Build();

    [Fact]
    public async Task Production_layer_and_contract_matrix_has_no_violations()
    {
        var model = ArchitectureTestBase.Architecture;
        var productionTypes = model
            .Types.Where(type =>
                type.Assembly.Name.StartsWith("Travel.Modules.", StringComparison.Ordinal)
                || type.Assembly.Name
                    is "Travel.Host"
                        or "Travel.AI"
                        or "Travel.AppHost"
                        or "Travel.ServiceDefaults"
                        or "Travel.Shared.Abstractions"
                        or "Travel.Shared.Domain"
                        or "Travel.Shared.Infrastructure"
                        or "Travel.Shared.Web"
            )
            .ToArray();
        productionTypes.ShouldNotBeEmpty();
        var productionAssemblies = ModuleArchitectureInventory
            .ExpectedAssemblyNames.Concat([
                "Travel.Host",
                "Travel.AI",
                "Travel.AppHost",
                "Travel.ServiceDefaults",
                "Travel.Shared.Abstractions",
                "Travel.Shared.Domain",
                "Travel.Shared.Infrastructure",
                "Travel.Shared.Web",
            ])
            .ToArray();
        var ilEdges = productionAssemblies
            .SelectMany(name =>
                IlTypeReferenceScanner.Scan(Path.Combine(AppContext.BaseDirectory, name + ".dll"))
            )
            .ToArray();
        var actualContractConsumers = ilEdges
            .Where(edge => edge.TargetAssembly == "Travel.IntegrationContracts.AI")
            .Select(edge => edge.OriginAssembly)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        actualContractConsumers.ShouldContain("Travel.Modules.Flights.Application");
        actualContractConsumers.ShouldContain("Travel.Modules.Flights.Api");
        actualContractConsumers.ShouldContain("Travel.AI");
        LayerArchitecturePolicy.FindLayerIlViolations(ilEdges).ShouldBeEmpty();
        LayerArchitecturePolicy.FindApiInfrastructureIlViolations(ilEdges).ShouldBeEmpty();
        LayerArchitecturePolicy.FindContractConsumerIlViolations(ilEdges).ShouldBeEmpty();
        LayerArchitecturePolicy.FindLayerTypeViolations(productionTypes).ShouldBeEmpty();
        LayerArchitecturePolicy
            .FindApiInfrastructureTypeViolations(productionTypes)
            .ShouldBeEmpty();
        LayerArchitecturePolicy.FindContractConsumerTypeViolations(productionTypes).ShouldBeEmpty();
        LayerArchitecturePolicy
            .FindImplementedApiSelectorViolations(productionTypes)
            .ShouldBeEmpty();

        foreach (var configuration in new[] { "Debug", "Release" })
        foreach (var source in ModuleArchitectureInventory.ExpectedProjectPaths(Root))
        {
            var references = await EvaluatedProjectReferences.ForProjectAsync(
                source,
                configuration
            );
            LayerArchitecturePolicy
                .FindProjectReferenceViolations(Root, source, references)
                .ShouldBeEmpty($"{source} ({configuration})");
        }
    }

    private static string FindRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
            if (File.Exists(Path.Combine(dir.FullName, "Travel.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Travel.slnx not found.");
    }
}
