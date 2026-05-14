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
            .Check(Arch);
    }

    // ─── Test 9: no ambient clock in Flights production code ──────────────────
    // Spec §17/§19: production code must use injected TimeProvider, never DateTime.UtcNow.
    // ArchUnitNET 0.13.x does not expose a narrow "NotCallMethod(type, getter)" predicate.
    // NotHaveDependencyInMethodBodyTo(Type) is too broad (fires for any record field of that
    // type). We use direct IL reflection: scan for call/callvirt tokens that resolve to the
    // exact static getter. This mirrors the pattern used in Test 8 for the TestOnly check.

    [Theory]
    [InlineData(typeof(DateTime), "get_UtcNow")]
    [InlineData(typeof(DateTime), "get_Now")]
    [InlineData(typeof(DateTimeOffset), "get_UtcNow")]
    [InlineData(typeof(DateTimeOffset), "get_Now")]
    public void Flights_production_code_does_not_use_ambient_clock(Type clock, string getter)
    {
        var violations = new List<string>();

        // Ensure Flights assemblies are loaded in the current AppDomain.
        // ArchitectureTestBase loads them lazily; accessing Architecture here forces loading.
        _ = Arch;

        var targets = new HashSet<string>(StringComparer.Ordinal)
        {
            "Travel.Modules.Flights.Core",
            "Travel.Modules.Flights.Application",
            "Travel.Modules.Flights.Infrastructure",
        };

        var flightsAssemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a => targets.Contains(a.GetName().Name ?? string.Empty))
            .ToArray();

        foreach (var asm in flightsAssemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                var methods = type.GetMethods(
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.DeclaredOnly
                );

                foreach (var method in methods)
                {
                    if (MethodCallsGetter(method, type.Module, clock, getter))
                        violations.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        violations.ShouldBeEmpty(
            $"Flights production code must not call {clock.Name}.{getter}. "
                + "Use injected TimeProvider instead.\nViolations:\n"
                + string.Join("\n", violations)
        );
    }

    /// <summary>
    /// Brute-force IL scan: reads every 5-byte window starting with opcode 0x28 (call) or
    /// 0x6F (callvirt) and attempts to resolve the following 4 bytes as a method token. The
    /// scan is window-based (not instruction-aligned) which can produce false token reads, but
    /// valid metadata tokens have a constrained high-byte range and the resolved method is
    /// compared by name + declaring type, making accidental matches practically impossible.
    /// </summary>
    private static bool MethodCallsGetter(
        System.Reflection.MethodInfo method,
        System.Reflection.Module module,
        Type clock,
        string getter
    )
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null || il.Length < 5)
            return false;

        for (int i = 0; i <= il.Length - 5; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F)
                continue;

            int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
            try
            {
                var callee = module.ResolveMethod(token);
                if (callee is not null && callee.Name == getter && callee.DeclaringType == clock)
                    return true;
            }
            catch
            {
                // Token unresolvable in this module context; skip.
            }
        }

        return false;
    }

    // ─── Test 10: Marten isolation ────────────────────────────────────────────
    // Spec §17: Marten (event store) is used only by Flights and Trips modules.

    [Fact]
    public void Only_Flights_and_Trips_depend_on_Marten()
    {
        Classes()
            .That()
            .ResideInNamespaceMatching(@"Travel\.Modules\.(Hotels|Rail|Identity)\..*")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"(Marten|JasperFx)\..*")
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
