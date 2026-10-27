using ArchUnitNET.Loader;
using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class TripsBriefContractArchitectureTests
{
    private const string Contract = "Travel.IntegrationContracts.AI";
    private const string TripsTarget = "Travel.IntegrationContracts.AI.TripsBriefExtraction";

    [Theory]
    [InlineData(
        "Travel.Modules.Trips.Application",
        "Travel.Modules.Trips.Application.BriefExtraction",
        "TripBriefCandidate",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Application",
        "Travel.Modules.Trips.Application.BriefExtraction.Nested",
        "Consumer",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Infrastructure",
        "Travel.Modules.Trips.Infrastructure.BriefExtraction",
        "NatsTripBriefExtractionClient",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Composition",
        "TripsModule",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Composition",
        "TripsBriefExtractionFeature",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Contracts",
        "TripBriefExtractionDtos",
        true
    )]
    [InlineData(
        "Travel.Modules.Trips.Core",
        "Travel.Modules.Trips.Core.BriefExtraction",
        "Consumer",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Application",
        "Travel.Modules.Trips.Application.Itineraries",
        "Consumer",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Application",
        "Travel.Modules.Trips.Application.BriefExtractionExtra",
        "Consumer",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Infrastructure",
        "Travel.Modules.Trips.Infrastructure.Itineraries",
        "Consumer",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.CompositionExtra",
        "TripsModule",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Composition",
        "Unrelated",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Composition",
        "TripsModuleExtra",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Contracts",
        "ItineraryCandidateResponseEncoder",
        false
    )]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Endpoints",
        "TripBriefCandidateEndpoint",
        false
    )]
    [InlineData("Travel.Host", "Travel.Host", "Program", false)]
    public void Trips_target_allowance_requires_exact_retained_origin(
        string assembly,
        string ns,
        string type,
        bool permitted
    )
    {
        var edge = new IlTypeEdge(
            assembly,
            ns,
            ns + "." + type,
            Contract,
            TripsTarget,
            TripsTarget + ".TripsBriefRequested"
        );
        var violations = LayerArchitecturePolicy.FindContractConsumerIlViolations([edge]);
        if (permitted)
            violations.ShouldBeEmpty();
        else
            violations.ShouldBe([edge.OriginType + " -> " + edge.TargetType]);
    }

    [Theory]
    [InlineData(
        "Travel.Modules.Trips.Application",
        "Travel.Modules.Trips.Application.BriefExtraction",
        "TripBriefCandidate"
    )]
    [InlineData(
        "Travel.Modules.Trips.Infrastructure",
        "Travel.Modules.Trips.Infrastructure.BriefExtraction",
        "NatsTripBriefExtractionClient"
    )]
    [InlineData("Travel.Modules.Trips.Api", "Travel.Modules.Trips.Api.Composition", "TripsModule")]
    [InlineData(
        "Travel.Modules.Trips.Api",
        "Travel.Modules.Trips.Api.Contracts",
        "TripBriefExtractionDtos"
    )]
    public void Trips_origins_never_gain_Flights_contracts_from_shared_assembly(
        string assembly,
        string ns,
        string type
    )
    {
        foreach (
            var target in new[] { "NlSearch.NlSearchRequested", "FareSummary.FareSummaryRequested" }
        )
        {
            var dot = target.LastIndexOf('.');
            var edge = new IlTypeEdge(
                assembly,
                ns,
                ns + "." + type,
                Contract,
                Contract + "." + target[..dot],
                Contract + "." + target
            );
            LayerArchitecturePolicy
                .FindContractConsumerIlViolations([edge])
                .ShouldBe([edge.OriginType + " -> " + edge.TargetType]);
        }
    }

    [Theory]
    [InlineData(
        "Travel.Modules.Flights.Application",
        "Travel.Modules.Flights.Application.Handlers.NlSearch"
    )]
    [InlineData("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Composition")]
    public void Existing_Flights_consumers_do_not_gain_Trips_contracts(string assembly, string ns)
    {
        var edge = new IlTypeEdge(
            assembly,
            ns,
            ns + ".Consumer",
            Contract,
            TripsTarget,
            TripsTarget + ".TripsBriefParsed"
        );
        LayerArchitecturePolicy
            .FindContractConsumerIlViolations([edge])
            .ShouldBe([edge.OriginType + " -> " + edge.TargetType]);
    }

    [Theory]
    [InlineData("Travel.Modules.Trips.Application.BriefExtraction.TripBriefCandidate")]
    [InlineData("Travel.Modules.Trips.Application.BriefExtraction.ITripBriefExtractionClient")]
    [InlineData("Travel.Modules.Trips.Application.BriefExtraction.TripBriefCandidateValidator")]
    [InlineData("Travel.Modules.Trips.Application.BriefExtraction.TripBriefDateNormalizer")]
    [InlineData(
        "Travel.Modules.Trips.Infrastructure.BriefExtraction.NatsTripBriefExtractionClient"
    )]
    [InlineData("Travel.Modules.Trips.Api.Composition.TripsModule")]
    [InlineData("Travel.Modules.Trips.Api.Composition.TripsBriefExtractionFeature")]
    public void Real_Trips_type_consumers_are_present_and_scoped(string name)
    {
        var type = ArchitectureTestBase.Architecture.Types.Single(type => type.FullName == name);
        type.Dependencies.ShouldContain(dependency =>
            dependency.Target.Assembly != null && dependency.Target.Assembly.Name == Contract
        );
        LayerArchitecturePolicy.FindContractConsumerTypeViolations([type]).ShouldBeEmpty();
    }

    [Fact]
    public void Actual_Api_encoder_method_body_dependencies_are_inspected_even_without_signature_edges()
    {
        var edges = IlTypeReferenceScanner
            .Scan(
                typeof(Travel.Modules.Trips.Api.Contracts.TripBriefExtractionDtos).Assembly.Location
            )
            .Where(edge => edge.TargetAssembly == Contract)
            .ToArray();
        edges.ShouldContain(edge =>
            edge.OriginType == "Travel.Modules.Trips.Api.Contracts.TripBriefExtractionDtos"
            && edge.TargetType == TripsTarget + ".TripsBriefFields"
        );
        LayerArchitecturePolicy.FindContractConsumerIlViolations(edges).ShouldBeEmpty();
        edges
            .Select(edge => edge.OriginType)
            .Distinct()
            .Order()
            .ShouldBe(
                new[]
                {
                    "Travel.Modules.Trips.Api.Composition.TripsBriefExtractionFeature",
                    "Travel.Modules.Trips.Api.Composition.TripsModule",
                    "Travel.Modules.Trips.Api.Contracts.TripBriefExtractionDtos",
                }.Order()
            );
    }

    [Theory]
    [InlineData(
        "Travel.Modules.Trips.Core.BriefExtraction.TripsCoreBriefContractFixture",
        "TripsBriefRequested"
    )]
    [InlineData(
        "Travel.Modules.Trips.Application.Itineraries.TripsItineraryBriefContractFixture",
        "TripsBriefParsed"
    )]
    [InlineData(
        "Travel.Modules.Trips.Api.Contracts.TripsUnrelatedBriefContractFixture",
        "TripsBriefFields"
    )]
    public void Type_fixtures_keep_Core_itineraries_and_unrelated_Api_outside_the_Trips_leaf(
        string name,
        string contractType
    )
    {
        var model = new ArchLoader()
            .LoadAssemblies(
                typeof(Travel.Modules.Trips.Core.BriefExtraction.TripsCoreBriefContractFixture).Assembly,
                typeof(Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefRequested).Assembly
            )
            .Build();
        var origin = model.Types.Single(type => type.FullName == name);
        LayerArchitecturePolicy
            .FindContractConsumerTypeViolations([origin])
            .ShouldBe([name + " -> " + TripsTarget + "." + contractType]);
    }
}
