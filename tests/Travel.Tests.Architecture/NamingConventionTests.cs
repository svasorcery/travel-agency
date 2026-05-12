using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Travel.Shared.Abstractions;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public class NamingConventionTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch = ArchitectureTestBase.Architecture;

    [Fact]
    public void Classes_in_Handlers_namespace_end_with_Handler()
    {
        Classes()
            .That().ResideInNamespaceMatching(@".*\.Handlers")
            .Should().HaveNameEndingWith("Handler")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Classes_in_Exceptions_namespace_end_with_Exception()
    {
        Classes()
            .That().ResideInNamespaceMatching(@".*\.Exceptions")
            .Should().HaveNameEndingWith("Exception")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Domain_event_classes_implement_IDomainEvent()
    {
        Classes()
            .That().HaveNameEndingWith("Event").And().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Core.*")
            .Should().ImplementInterface(typeof(IDomainEvent))
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }
}
