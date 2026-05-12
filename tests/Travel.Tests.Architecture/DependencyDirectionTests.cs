using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public class DependencyDirectionTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch = ArchitectureTestBase.Architecture;

    [Fact]
    public void Core_layers_must_not_depend_on_Infrastructure()
    {
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Core.*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Infrastructure.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Application_layers_must_not_depend_on_Api()
    {
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Application.*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Api.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Infrastructure_layers_must_not_depend_on_Api()
    {
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Infrastructure.*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.Api.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Core_and_Application_layers_must_not_depend_on_Travel_Shared_Web()
    {
        // Travel.Shared.Web carries AspNetCore framework reference (ProblemDetails etc).
        // Only Api layer and apps/ may consume it; Domain/Application must stay web-free
        // so they can be reused in non-HTTP hosts (background workers, AI service).
        Classes()
            .That().ResideInNamespaceMatching(@"Travel\.Modules\.\w+\.(Core|Application).*")
            .Should().NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Shared\.Web.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }
}
