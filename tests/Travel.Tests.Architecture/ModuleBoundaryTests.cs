using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public class ModuleBoundaryTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch =
        ArchitectureTestBase.Architecture;

    [Fact]
    public void Flights_module_does_not_depend_on_Hotels_internals()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Hotels.*")
            .Check(Arch);
    }

    [Fact]
    public void Hotels_module_does_not_depend_on_Flights_internals()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Hotels.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights.*")
            .Check(Arch);
    }

    [Fact]
    public void Rail_module_does_not_depend_on_other_module_internals()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Rail.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.(Flights|Hotels|Trips|Identity).*")
            .Check(Arch);
    }

    [Fact]
    public void Trips_module_does_not_depend_on_other_module_internals()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Trips.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.(Flights|Hotels|Rail|Identity).*")
            .Check(Arch);
    }
}
