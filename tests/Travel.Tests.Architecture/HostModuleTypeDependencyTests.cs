using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Shouldly;
using Xunit;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class HostModuleTypeDependencyTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch =
        ArchitectureTestBase.Architecture;
    private static readonly ReflectionAssembly HostAssembly = ReflectionAssembly.LoadFrom(
        Path.Combine(AppContext.BaseDirectory, "Travel.Host.dll")
    );

    [Fact]
    public void Host_all_types_depend_only_on_module_Api_Composition()
    {
        var hostAssemblyName = HostAssembly.GetName().Name;
        var hostTypes = Arch
            .Types.Where(type =>
                string.Equals(type.Assembly.Name, hostAssemblyName, StringComparison.Ordinal)
            )
            .ToArray();

        hostTypes.ShouldNotBeEmpty();
        FindModuleTypeDependencyViolations(hostTypes).ShouldBeEmpty();
    }

    [Fact]
    public void Host_Api_namespace_guard_rejects_controlled_Endpoint_dependency()
    {
        var mutationArchitecture = new ArchLoader()
            .LoadAssemblies(
                typeof(HostApiEndpointDependencyBypassFixture).Assembly,
                typeof(Travel.Modules.Flights.Api.Endpoints.SearchEndpoint).Assembly
            )
            .Build();
        var fixtureTypes = mutationArchitecture
            .Types.Where(type =>
                string.Equals(
                    type.FullName,
                    typeof(HostApiEndpointDependencyBypassFixture).FullName,
                    StringComparison.Ordinal
                )
            )
            .ToArray();

        fixtureTypes.ShouldNotBeEmpty();
        FindModuleTypeDependencyViolations(fixtureTypes)
            .ShouldHaveSingleItem()
            .ShouldContain("Travel.Modules.Flights.Api.Endpoints.SearchEndpoint");
    }

    private static string[] FindModuleTypeDependencyViolations(IEnumerable<IType> sourceTypes) =>
        sourceTypes
            .SelectMany(type => type.Dependencies)
            .Where(dependency => IsForbiddenModuleType(dependency.Target))
            .Select(dependency => $"{dependency.Origin.FullName} -> {dependency.Target.FullName}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToArray();

    private static bool IsForbiddenModuleType(IType target)
    {
        var moduleNamespace = target.Namespace?.FullName;
        if (
            moduleNamespace is null
            || !moduleNamespace.StartsWith("Travel.Modules.", StringComparison.Ordinal)
        )
            return false;

        var segments = moduleNamespace.Split('.');
        return segments.Length < 5
            || !string.Equals(segments[3], "Api", StringComparison.Ordinal)
            || !string.Equals(segments[4], "Composition", StringComparison.Ordinal);
    }
}

internal sealed class HostApiEndpointDependencyBypassFixture
{
    public Travel.Modules.Flights.Api.Endpoints.SearchEndpoint? Endpoint { get; init; }
}
