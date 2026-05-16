using System.IO;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Travel.Shared.Abstractions;
using Travel.Shared.Domain;
using Travel.Shared.Infrastructure.Initialization;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Travel.Tests.Architecture;

public static class ArchitectureTestBase
{
    // Lazily build the ArchUnit architecture model by scanning the test's own bin directory.
    // This approach automatically picks up all 20 module assemblies (4 layers × 5 modules)
    // as well as the 4 shared assemblies, without requiring individual typeof() markers per layer.
    // Subproject 1 (Flights) has real classes — Flights rules no longer use .WithoutRequiringPositiveResults().
    // Other modules' rules stay permissive until those modules are implemented.
    private static readonly Lazy<global::ArchUnitNET.Domain.Architecture> ArchitectureLoader = new(
        () =>
        {
            var baseDir = AppContext.BaseDirectory;

            // Scan for all Travel.Modules.*.dll files that are NOT test assemblies.
            var moduleAssemblies = Directory
                .GetFiles(baseDir, "Travel.Modules.*.dll", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).Contains(".Tests."))
                .Select(ReflectionAssembly.LoadFrom)
                .ToArray();

            // Anchor shared assemblies via well-known types so the compiler enforces
            // they remain referenced by this test project (and thus present in bin/).
            var sharedAssemblies = new[]
            {
                typeof(IDomainEvent).Assembly, // Travel.Shared.Abstractions
                typeof(AggregateRoot<>).Assembly, // Travel.Shared.Domain
                typeof(IInitializer).Assembly, // Travel.Shared.Infrastructure
            };

            // Travel.Shared.Web carries an AspNetCore reference; load it via path
            // rather than a compile-time typeof() to avoid pulling Mvc types into the
            // test project's own namespace.  The ProjectReference in .csproj already
            // ensures the dll lands in bin/.
            var allShared = sharedAssemblies.ToList();
            var sharedWebPath = Path.Combine(baseDir, "Travel.Shared.Web.dll");
            if (File.Exists(sharedWebPath))
                allShared.Add(ReflectionAssembly.LoadFrom(sharedWebPath));

            return new ArchLoader()
                .LoadAssemblies(moduleAssemblies.Concat(allShared).ToArray())
                .Build();
        }
    );

    public static global::ArchUnitNET.Domain.Architecture Architecture => ArchitectureLoader.Value;
}
