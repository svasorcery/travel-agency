using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class HostModuleProjectReferenceTests
{
    private static readonly string HostProject = Project("apps/Travel.Host/Travel.Host.csproj");

    [Fact]
    public async Task Host_references_only_module_Api_facades()
    {
        var expected = new[]
        {
            Project("modules/flights/Travel.Modules.Flights.Api/Travel.Modules.Flights.Api.csproj"),
            Project("modules/support/Travel.Modules.Support.Api/Travel.Modules.Support.Api.csproj"),
            Project("modules/hotels/Travel.Modules.Hotels.Api/Travel.Modules.Hotels.Api.csproj"),
            Project("modules/trips/Travel.Modules.Trips.Api/Travel.Modules.Trips.Api.csproj"),
            Project("modules/rail/Travel.Modules.Rail.Api/Travel.Modules.Rail.Api.csproj"),
            Project(
                "modules/identity/Travel.Modules.Identity.Api/Travel.Modules.Identity.Api.csproj"
            ),
        };

        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var actual = (
                await EvaluatedProjectReferences.ForProjectAsync(HostProject, configuration)
            )
                .Where(IsModuleProject)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            actual.ShouldBe(
                expected.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                ignoreOrder: false
            );
        }
    }

    [Fact]
    public async Task Host_closure_reaches_every_layer_of_implemented_Rail_and_Trips()
    {
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var closure = await EvaluatedProjectReferences.ForProjectClosureAsync(
                HostProject,
                configuration
            );
            foreach (var module in new[] { ("rail", "Rail"), ("trips", "Trips") })
            foreach (var layer in new[] { "Core", "Application", "Infrastructure", "Api" })
                closure.ShouldContain(
                    Project(
                        $"modules/{module.Item1}/Travel.Modules.{module.Item2}.{layer}/Travel.Modules.{module.Item2}.{layer}.csproj"
                    )
                );
        }
    }

    [Fact]
    public async Task Project_closure_detects_transitive_scaffold_module_bypass()
    {
        var fixture = Project(
            "tests/Travel.Tests.Architecture/Fixtures/HostModuleClosure/Root.proj"
        );

        var closure = await EvaluatedProjectReferences.ForProjectClosureAsync(fixture, "Release");

        closure.ShouldContain(path =>
            Path.GetFileName(path).StartsWith("Travel.Modules.Hotels.", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("rail", "Rail")]
    [InlineData("trips", "Trips")]
    public async Task Implemented_Api_references_its_own_Infrastructure(
        string folder,
        string module
    )
    {
        var project = Project(
            $"modules/{folder}/Travel.Modules.{module}.Api/Travel.Modules.{module}.Api.csproj"
        );
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var references = await EvaluatedProjectReferences.ForProjectAsync(
                project,
                configuration
            );
            references.ShouldContain(
                Project(
                    $"modules/{folder}/Travel.Modules.{module}.Infrastructure/Travel.Modules.{module}.Infrastructure.csproj"
                )
            );
        }
    }

    private static string Project(string relativePath) =>
        Path.GetFullPath(Path.Combine(RepositoryRoot, relativePath));

    private static bool IsModuleProject(string path) =>
        path.StartsWith(
            Path.Combine(RepositoryRoot, "modules") + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        );

    private static string RepositoryRoot
    {
        get
        {
            for (
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                directory is not null;
                directory = directory.Parent
            )
            {
                if (File.Exists(Path.Combine(directory.FullName, "Travel.slnx")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repository root containing Travel.slnx."
            );
        }
    }
}
