using System.IO;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Travel.Shared.Abstractions;
using Travel.Shared.Domain;
using Travel.Shared.Infrastructure.Initialization;
using Travel.Tests.Architecture.Support;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Travel.Tests.Architecture;

public static class ArchitectureTestBase
{
    // Lazily build the ArchUnit architecture model by scanning the test's own bin directory.
    // This approach automatically picks up all 20 module assemblies (4 layers × 5 modules)
    // as well as the 5 shared assemblies, without requiring individual typeof() markers per layer.
    // Subproject 1 (Flights) has real classes — Flights rules no longer use .WithoutRequiringPositiveResults().
    // Other modules' rules stay permissive until those modules are implemented.
    private static readonly Lazy<global::ArchUnitNET.Domain.Architecture> ArchitectureLoader = new(
        () =>
        {
            var baseDir = AppContext.BaseDirectory;

            // Load the exact module inventory; missing/extra DLLs fail before a selector can pass vacuously.
            var discoveredNames = Directory
                .GetFiles(baseDir, "Travel.Modules.*.dll", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).Contains(".Tests."))
                .Select(Path.GetFileNameWithoutExtension)
                .ToArray();
            var inventoryViolations = ModuleArchitectureInventory.FindAssemblyInventoryViolations(
                discoveredNames
            );
            if (inventoryViolations.Length != 0)
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, inventoryViolations)
                );
            var moduleAssemblies = ModuleArchitectureInventory
                .ExpectedAssemblyNames.Select(name =>
                    ReflectionAssembly.LoadFrom(Path.Combine(baseDir, name + ".dll"))
                )
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
            foreach (
                var sharedAssemblyName in new[]
                {
                    "Travel.Shared.Web.dll",
                    "Travel.Shared.TestInfrastructure.dll",
                }
            )
            {
                var sharedAssemblyPath = Path.Combine(baseDir, sharedAssemblyName);
                if (!File.Exists(sharedAssemblyPath))
                    throw new FileNotFoundException(
                        $"The Architecture test output must contain {sharedAssemblyName}.",
                        sharedAssemblyPath
                    );
                allShared.Add(ReflectionAssembly.LoadFrom(sharedAssemblyPath));
            }

            var hostAssemblyPath = Path.Combine(baseDir, "Travel.Host.dll");
            if (!File.Exists(hostAssemblyPath))
                throw new FileNotFoundException(
                    "The Architecture test output must contain Travel.Host.dll.",
                    hostAssemblyPath
                );

            var serviceDefaultsAssemblyPath = Path.Combine(baseDir, "Travel.ServiceDefaults.dll");
            if (!File.Exists(serviceDefaultsAssemblyPath))
                throw new FileNotFoundException(
                    "The Architecture test output must contain Travel.ServiceDefaults.dll.",
                    serviceDefaultsAssemblyPath
                );

            var aiAssemblyPath = Path.Combine(baseDir, "Travel.AI.dll");
            var appHostAssemblyPath = Path.Combine(baseDir, "Travel.AppHost.dll");
            var contractAssemblyPath = Path.Combine(baseDir, "Travel.IntegrationContracts.AI.dll");
            foreach (
                var requiredPath in new[]
                {
                    aiAssemblyPath,
                    appHostAssemblyPath,
                    contractAssemblyPath,
                }
            )
                if (!File.Exists(requiredPath))
                    throw new FileNotFoundException(
                        $"The Architecture test output must contain {Path.GetFileName(requiredPath)}.",
                        requiredPath
                    );
            return new ArchLoader()
                .LoadAssemblies(
                    moduleAssemblies
                        .Concat(allShared)
                        .Append(ReflectionAssembly.LoadFrom(hostAssemblyPath))
                        .Append(ReflectionAssembly.LoadFrom(serviceDefaultsAssemblyPath))
                        .Append(ReflectionAssembly.LoadFrom(aiAssemblyPath))
                        .Append(ReflectionAssembly.LoadFrom(appHostAssemblyPath))
                        .Append(ReflectionAssembly.LoadFrom(contractAssemblyPath))
                        .ToArray()
                )
                .Build();
        }
    );

    public static global::ArchUnitNET.Domain.Architecture Architecture => ArchitectureLoader.Value;
}
