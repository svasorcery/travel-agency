using ArchUnitNET.Loader;
using Shouldly;
using Travel.Tests.Architecture.Support;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class DomainEventMatrixTests
{
    [Fact]
    public void Every_loaded_module_Core_domain_event_uses_the_namespace_and_interface()
    {
        var coreTypes = ArchitectureTestBase
            .Architecture.Types.Where(type =>
                type.Assembly.Name.StartsWith("Travel.Modules.", StringComparison.Ordinal)
                && type.Assembly.Name.EndsWith(".Core", StringComparison.Ordinal)
            )
            .ToArray();
        coreTypes.ShouldNotBeEmpty();
        LayerArchitecturePolicy.FindDomainEventViolations(coreTypes).ShouldBeEmpty();
        LayerArchitecturePolicy.FindMissingRequiredDomainEvents(coreTypes).ShouldBeEmpty();
    }

    [Fact]
    public void Event_namespace_selects_non_Event_names_and_rejects_non_events()
    {
        var model = new ArchLoader()
            .LoadAssemblies(
                typeof(Travel.Modules.Flights.Core.DomainEvents.InvalidDomainEventFixture).Assembly,
                typeof(Travel.Modules.Flights.Core.DomainEvents.OfferHeld).Assembly
            )
            .Build();
        var fixture = model.Types.Single(type =>
            type.FullName
            == typeof(Travel.Modules.Flights.Core.DomainEvents.InvalidDomainEventFixture).FullName
        );
        LayerArchitecturePolicy
            .FindDomainEventViolations([fixture])
            .ShouldContain(violation =>
                violation.Contains("InvalidDomainEventFixture", StringComparison.Ordinal)
            );

        var realEvents = model
            .Types.Where(type => type.Assembly.Name == "Travel.Modules.Flights.Core")
            .ToArray();
        LayerArchitecturePolicy.FindDomainEventViolations(realEvents).ShouldBeEmpty();
        LayerArchitecturePolicy.FindMissingRequiredDomainEvents(realEvents).ShouldBeEmpty();
    }
}
