using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Abstractions;

namespace Travel.Shared.Web;

/// <summary>
/// Guards against [TestOnly] implementation types being registered in a Production
/// IServiceCollection. Call <see cref="Verify"/> after all services are registered
/// and before the host is built.
/// </summary>
public static class TestOnlyGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any service descriptor in
    /// <paramref name="services"/> has an implementation type decorated with
    /// <see cref="TestOnlyAttribute"/> and the current environment is Production.
    /// </summary>
    public static void Verify(IServiceCollection services, IWebHostEnvironment environment)
    {
        if (!environment.IsProduction())
            return;

        var violations = services
            .Select(d => d.ImplementationType)
            .OfType<Type>()
            .Where(t => t.IsDefined(typeof(TestOnlyAttribute), inherit: false))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"[TestOnly] types must not be registered in Production. Violations: {string.Join(", ", violations)}"
            );
        }
    }
}
