using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Travel.Shared.Abstractions;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture.Flights;

[Trait("Category", "Architecture")]
public sealed class TestOnlyAttributeTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch =
        ArchitectureTestBase.Architecture;

    [Fact]
    public void TestOnly_attribute_exists_in_Shared_Abstractions()
    {
        var classes = Classes()
            .That()
            .HaveName(nameof(TestOnlyAttribute))
            .And()
            .ResideInAssembly(typeof(IDomainEvent).Assembly);

        classes.Should().Exist().Check(Arch);
    }

    [Fact]
    public void TestOnly_attribute_inherits_from_System_Attribute()
    {
        var rule = Classes()
            .That()
            .HaveName(nameof(TestOnlyAttribute))
            .Should()
            .BeAssignableTo(typeof(Attribute));

        rule.Check(Arch);
    }
}
