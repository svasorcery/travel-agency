using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class ModuleMatrixTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Enabled_support_has_four_layers_in_the_exact_six_module_inventory()
    {
        var paths = ModuleArchitectureInventory.ExpectedProjectPaths(Root);
        paths.Length.ShouldBe(24);
        paths
            .Count(path => path.Contains("Travel.Modules.Support.", StringComparison.Ordinal))
            .ShouldBe(4);
        ModuleArchitectureInventory.ExpectedAssemblyNames.Length.ShouldBe(24);
        ModuleArchitectureInventory
            .FindAssemblyInventoryViolations(
                ModuleArchitectureInventory.ExpectedAssemblyNames.Where(name =>
                    name != "Travel.Modules.Support.Api"
                )
            )
            .ShouldContain(value =>
                value.Contains("Travel.Modules.Support.Api", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void Inventory_rejects_an_unlisted_module_and_a_missing_assembly()
    {
        var unexpected = Path.Combine(
            Root,
            "modules",
            "sixth",
            "Travel.Modules.Sixth.Core",
            "Travel.Modules.Sixth.Core.csproj"
        );
        ModuleArchitectureInventory
            .FindProjectInventoryViolations(
                Root,
                ModuleArchitectureInventory.ExpectedProjectPaths(Root).Append(unexpected)
            )
            .ShouldContain(violation => violation.Contains("Sixth", StringComparison.Ordinal));

        ModuleArchitectureInventory
            .FindAssemblyInventoryViolations(
                ModuleArchitectureInventory.ExpectedAssemblyNames.Where(name =>
                    name != "Travel.Modules.Identity.Api"
                )
            )
            .ShouldContain(violation =>
                violation.Contains("Travel.Modules.Identity.Api", StringComparison.Ordinal)
            );
    }

    [Theory]
    [InlineData("Debug", false, "identity", "Identity", "Api")]
    [InlineData("Release", true, "identity", "Identity", "Api")]
    [InlineData("Debug", false, "trips", "Trips", "Application")]
    [InlineData("Release", true, "trips", "Trips", "Application")]
    public async Task Imported_foreign_reference_is_detected_in_its_effective_configuration(
        string configuration,
        bool expectedViolation,
        string folder,
        string module,
        string layer
    )
    {
        var fixture = Path.Combine(
            Root,
            "tests",
            "Travel.Tests.Architecture",
            "Fixtures",
            "ModuleMatrix",
            "Source.proj"
        );
        var references = await EvaluatedProjectReferences.ForProjectAsync(fixture, configuration);
        var identityApi = Path.Combine(
            Root,
            "modules",
            folder,
            $"Travel.Modules.{module}.{layer}",
            $"Travel.Modules.{module}.{layer}.csproj"
        );
        var violations = ModuleArchitectureInventory.FindCrossModuleProjectReferenceViolations(
            Root,
            identityApi,
            references
        );

        if (expectedViolation)
            violations.ShouldContain(violation =>
                violation.Contains("Flights.Core", StringComparison.Ordinal)
                && violation.Contains($"{module}.{layer}", StringComparison.Ordinal)
            );
        else
            violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("Application")]
    [InlineData("Infrastructure")]
    public void Trips_itinerary_selector_cannot_disappear(string layer)
    {
        var types = ArchitectureTestBase.Architecture.Types.ToArray();
        ModuleArchitectureInventory.FindImplementedSelectorViolations(types).ShouldBeEmpty();
        var assembly = $"Travel.Modules.Trips.{layer}";
        var itineraryNamespace = assembly + ".Itineraries";
        var selected = types
            .Where(type =>
                type.Assembly.Name == assembly
                && (
                    type.Namespace?.FullName == itineraryNamespace
                    || type.Namespace?.FullName.StartsWith(
                        itineraryNamespace + ".",
                        StringComparison.Ordinal
                    ) == true
                )
            )
            .ToArray();
        selected.ShouldNotBeEmpty();
        ModuleArchitectureInventory
            .FindImplementedSelectorViolations(types.Except(selected))
            .ShouldBe([$"implemented selector is empty: {assembly} / {itineraryNamespace}"]);
    }

    [Theory]
    [InlineData("Flights", "Travel.Modules.Flights.Core.ValueObjects.IataCode")]
    [InlineData("Hotels", "Travel.Modules.Hotels.Core.HotelPropertyReference")]
    [InlineData("Rail", "Travel.Modules.Rail.Core.StationRef")]
    public void Trips_application_rejects_each_foreign_module_internal(
        string targetModule,
        string targetType
    )
    {
        var originName =
            typeof(Travel.Modules.Trips.Application.Itineraries.TripsForeignInternalsFixture).FullName;
        var model = new ArchLoader()
            .LoadAssemblies(
                typeof(Travel.Modules.Trips.Application.Itineraries.TripsForeignInternalsFixture).Assembly,
                typeof(Travel.Modules.Flights.Core.ValueObjects.IataCode).Assembly,
                typeof(Travel.Modules.Hotels.Core.HotelPropertyReference).Assembly,
                typeof(Travel.Modules.Rail.Core.StationRef).Assembly
            )
            .Build();
        var origin = model.Types.Single(type => type.FullName == originName);
        ModuleArchitectureInventory
            .FindCrossModuleTypeDependencyViolations([origin])
            .Where(value => value.Contains(targetType, StringComparison.Ordinal))
            .ShouldBe([$"{originName} -> {targetType} (Trips -> {targetModule})"]);
    }

    [Fact]
    public void Interface_origin_with_a_foreign_type_dependency_is_rejected()
    {
        var model = new ArchLoader()
            .LoadAssemblies(
                typeof(Travel.Modules.Hotels.Application.ForeignModuleInterfaceFixture).Assembly,
                typeof(Travel.Modules.Flights.Core.ValueObjects.IataCode).Assembly
            )
            .Build();
        var origin = model.Types.Single(type =>
            type.FullName
            == typeof(Travel.Modules.Hotels.Application.ForeignModuleInterfaceFixture).FullName
        );

        ModuleArchitectureInventory
            .FindCrossModuleTypeDependencyViolations([origin])
            .ShouldContain(violation =>
                violation.Contains("Hotels.Application", StringComparison.Ordinal)
                && violation.Contains("Flights.Core", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task Production_matrix_has_all_projects_assemblies_and_no_cross_module_edges()
    {
        var actualProjects = Directory
            .EnumerateFiles(Path.Combine(Root, "modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .ToArray();
        ModuleArchitectureInventory
            .FindProjectInventoryViolations(Root, actualProjects)
            .ShouldBeEmpty();

        var model = ArchitectureTestBase.Architecture;
        var actualAssemblies = Directory
            .GetFiles(AppContext.BaseDirectory, "Travel.Modules.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests."))
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();
        ModuleArchitectureInventory
            .FindAssemblyInventoryViolations(actualAssemblies)
            .ShouldBeEmpty();

        var origins = model
            .Types.Where(type =>
                ModuleArchitectureInventory.ExpectedAssemblyNames.Contains(
                    type.Assembly.Name,
                    StringComparer.Ordinal
                )
            )
            .ToArray();
        ModuleArchitectureInventory.FindImplementedSelectorViolations(origins).ShouldBeEmpty();
        ModuleArchitectureInventory
            .FindCrossModuleTypeDependencyViolations(origins)
            .ShouldBeEmpty();
        var methodBodyEdges = ModuleArchitectureInventory
            .ExpectedAssemblyNames.SelectMany(name =>
                IlTypeReferenceScanner.Scan(Path.Combine(AppContext.BaseDirectory, name + ".dll"))
            )
            .ToArray();
        ModuleArchitectureInventory.FindCrossModuleIlViolations(methodBodyEdges).ShouldBeEmpty();

        foreach (var configuration in new[] { "Debug", "Release" })
        foreach (var source in ModuleArchitectureInventory.ExpectedProjectPaths(Root))
        {
            var references = await EvaluatedProjectReferences.ForProjectAsync(
                source,
                configuration
            );
            ModuleArchitectureInventory
                .FindCrossModuleProjectReferenceViolations(Root, source, references)
                .ShouldBeEmpty($"{source} ({configuration})");
        }
    }

    [Fact]
    public void Generic_placeholder_classification_preserves_foreign_constraints_and_arguments()
    {
        var edges = IlTypeReferenceScanner
            .Scan(
                typeof(Travel.Modules.Hotels.Application.ForeignGenericConstraintsFixture<>)
                    .Assembly
                    .Location
            )
            .Where(edge =>
                edge.OriginType.Contains(
                    "ForeignGenericConstraintsFixture",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        var violations = ModuleArchitectureInventory.FindCrossModuleIlViolations(edges);
        violations.ShouldContain(value =>
            value.Contains("IFlightSearchProvider", StringComparison.Ordinal)
        );
        violations.ShouldContain(value =>
            value.Contains("IFlightBookingProvider", StringComparison.Ordinal)
        );
        violations.ShouldContain(value => value.Contains("IataCode", StringComparison.Ordinal));
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
