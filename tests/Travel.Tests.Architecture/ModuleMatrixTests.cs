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
    [InlineData("Debug", false)]
    [InlineData("Release", true)]
    public async Task Imported_foreign_reference_is_detected_in_its_effective_configuration(
        string configuration,
        bool expectedViolation
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
            "identity",
            "Travel.Modules.Identity.Api",
            "Travel.Modules.Identity.Api.csproj"
        );
        var violations = ModuleArchitectureInventory.FindCrossModuleProjectReferenceViolations(
            Root,
            identityApi,
            references
        );

        if (expectedViolation)
            violations.ShouldContain(violation =>
                violation.Contains("Flights.Core", StringComparison.Ordinal)
                && violation.Contains("Identity.Api", StringComparison.Ordinal)
            );
        else
            violations.ShouldBeEmpty();
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
