using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Abstractions;

namespace Travel.ServiceDefaults.Hosting;

/// <summary>
/// Guards against test-only implementations being registered in a Production host.
/// </summary>
public static class TestOnlyGuard
{
    public static void Verify(IServiceCollection services, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
            return;

        var violations = services
            .Select(descriptor => descriptor.ImplementationType)
            .OfType<Type>()
            .Where(type => type.IsDefined(typeof(TestOnlyAttribute), inherit: false))
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        if (violations.Length > 0)
        {
            throw new InvalidOperationException(
                $"[TestOnly] types must not be registered in Production. Violations: {string.Join(", ", violations)}"
            );
        }
    }
}
