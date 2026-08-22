using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class ApiCompositionBoundaryTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch =
        ArchitectureTestBase.Architecture;

    [Theory]
    [InlineData("Endpoints")]
    [InlineData("Contracts")]
    [InlineData("Middleware")]
    public void Flights_Api_transport_types_do_not_depend_on_Infrastructure(string area)
    {
        var transportTypes = Classes()
            .That()
            .ResideInNamespaceMatching($@"Travel\.Modules\.Flights\.Api\.{area}.*");

        transportTypes.Should().Exist().Check(Arch);
        transportTypes
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Infrastructure.*")
            .Check(Arch);
    }

    [Fact]
    public void All_Flights_Api_types_outside_Composition_do_not_depend_on_Infrastructure()
    {
        var nonCompositionTypes = Classes()
            .That()
            .ResideInNamespaceMatching(
                @"^Travel\.Modules\.Flights\.Api(?:$|\.(?!Composition(?:\.|$)).*)"
            );

        nonCompositionTypes.Should().Exist().Check(Arch);
        nonCompositionTypes
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Infrastructure.*")
            .Check(Arch);
    }
}
