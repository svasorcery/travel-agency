using System.Collections;
using System.Reflection;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundDeliveryPolicyTests
{
    [Fact]
    public void Configuration_installs_the_customer_policy_without_a_global_cancellation_policy()
    {
        var options = new WolverineOptions();
        CustomerRefundDeliveryPolicy.Configure(options);
        var configured = new HashSet<Type>();
        Inspect(options, configured, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        configured.ShouldContain(typeof(CustomerRefundDeliveryPolicy));
        configured.ShouldNotContain(typeof(CancellationDeliveryPolicy));
    }

    private static void Inspect(object? value, HashSet<Type> found, HashSet<object> seen, int depth)
    {
        if (value is null || depth > 8 || value is string || !seen.Add(value))
            return;
        if (value is Type type)
        {
            if (typeof(IHandlerPolicy).IsAssignableFrom(type))
                found.Add(type);
            return;
        }
        if (value is IHandlerPolicy)
            found.Add(value.GetType());
        if (value is IEnumerable items)
        {
            foreach (var item in items)
                Inspect(item, found, seen, depth + 1);
            return;
        }
        var current = value.GetType();
        if (current.Namespace?.StartsWith("Wolverine", StringComparison.Ordinal) != true)
            return;
        while (current is not null)
        {
            foreach (
                var f in current.GetFields(
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                )
            )
                Inspect(f.GetValue(value), found, seen, depth + 1);
            current = current.BaseType;
        }
    }
}
