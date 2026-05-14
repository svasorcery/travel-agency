using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Payments;
using Travel.Shared.Abstractions;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture.Flights;

[Trait("Category", "Architecture")]
public sealed class FlightsArchitectureTests
{
    private static readonly global::ArchUnitNET.Domain.Architecture Arch =
        ArchitectureTestBase.Architecture;

    // ─── Test 1: Core layer purity ────────────────────────────────────────────

    [Fact]
    public void Flights_Core_does_not_depend_on_Application()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Application.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_Core_does_not_depend_on_Infrastructure()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Infrastructure.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_Core_does_not_depend_on_Api()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Api.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 2: Application layer ────────────────────────────────────────────
    // Application MAY depend on Marten/Wolverine (Critter Stack pattern). The
    // rule only forbids depending on the sibling Infrastructure and Api layers.

    [Fact]
    public void Flights_Application_does_not_depend_on_Infrastructure()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Application.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Infrastructure.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_Application_does_not_depend_on_Api()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Application.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Api.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 3: Infrastructure layer ─────────────────────────────────────────

    [Fact]
    public void Flights_Infrastructure_does_not_depend_on_Api()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Infrastructure.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Api.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 4: Cross-module isolation ───────────────────────────────────────

    [Fact]
    public void Flights_module_does_not_depend_on_Hotels()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Hotels.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_module_does_not_depend_on_Rail()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Rail.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_module_does_not_depend_on_Trips()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Trips.*")
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 5: Domain events implement IDomainEvent ─────────────────────────
    // Targets all classes in the DomainEvents namespace, excluding enums
    // (CancelReason, RefundInitiator) which ArchUnitNET does not include via
    // Classes() — so the filter is naturally correct.

    [Fact]
    public void Flights_domain_events_implement_IDomainEvent()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core\.DomainEvents.*")
            .Should()
            .ImplementInterface(typeof(IDomainEvent))
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 6: Value objects are sealed records ─────────────────────────────
    // Records compile to classes, so the ArchUnitNET Classes() predicate covers them.
    // The abstract Offer base record is the one intentional exception — it MUST be
    // abstract (to enable polymorphism) and cannot be sealed. We assert:
    // every class in the ValueObjects namespace is SEALED OR ABSTRACT.
    // readonly record structs are not picked up by Classes() so they are fine either way.

    [Fact]
    public void Flights_value_objects_are_sealed_or_abstract()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core\.ValueObjects.*")
            .Should()
            .BeSealed()
            .OrShould()
            .BeAbstract()
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 7: Provider DTOs stay in Infrastructure ─────────────────────────
    // Core and Application must not reference Duffel or Travelpayouts DTO types.

    [Fact]
    public void Flights_Core_does_not_depend_on_Duffel_Dto()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(
                @"Travel\.Modules\.Flights\.Infrastructure\.Providers\.Duffel\.Dto.*"
            )
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_Application_does_not_depend_on_Duffel_Dto()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Application.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(
                @"Travel\.Modules\.Flights\.Infrastructure\.Providers\.Duffel\.Dto.*"
            )
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_Core_does_not_depend_on_Travelpayouts_Dto()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Core.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(
                @"Travel\.Modules\.Flights\.Infrastructure\.Providers\.Travelpayouts\.Dto.*"
            )
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    [Fact]
    public void Flights_Application_does_not_depend_on_Travelpayouts_Dto()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.Application.*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(
                @"Travel\.Modules\.Flights\.Infrastructure\.Providers\.Travelpayouts\.Dto.*"
            )
            .WithoutRequiringPositiveResults()
            .Check(Arch);
    }

    // ─── Test 8: [TestOnly] payment gateway marker presence ───────────────────
    // This is a MARKER-PRESENCE check: we assert that DuffelTestWalletPaymentGateway
    // carries [TestOnly] so that automated review tooling and the runtime guard in
    // Program.cs can rely on it. A full DI-call-graph analysis (asserting the type is
    // never registered via AddSingleton/AddScoped) is out of scope for M1 structural
    // tests. The complementary runtime enforcement is provided by TestOnlyGuard in
    // apps/Travel.Host/Program.cs: it throws if any [TestOnly] implementation type is
    // found in the IServiceCollection while ASPNETCORE_ENVIRONMENT == Production.

    [Fact]
    public void DuffelTestWalletPaymentGateway_carries_TestOnly_attribute()
    {
        // ArchUnitNET 0.13.x does not expose a fluent HaveCustomAttribute(...) on
        // ClassesShould, so we use direct reflection here. This is a deliberate
        // approximation: the structural guarantee (marker present, runtime guard
        // in TestOnlyGuard can find the type) is equivalent.
        typeof(DuffelTestWalletPaymentGateway)
            .IsDefined(typeof(TestOnlyAttribute), inherit: false)
            .ShouldBeTrue(
                "DuffelTestWalletPaymentGateway must carry [TestOnly] so the runtime "
                    + "guard in TestOnlyGuard can detect it if it is accidentally registered "
                    + "in Production."
            );
    }
}
