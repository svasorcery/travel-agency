using ArchUnitNET.Domain;

namespace Travel.Tests.Architecture.Support;

internal static class ModuleArchitectureInventory
{
    private sealed record Module(string Name, string Folder);

    private static readonly Module[] Modules =
    [
        new("Flights", "flights"),
        new("Hotels", "hotels"),
        new("Rail", "rail"),
        new("Trips", "trips"),
        new("Identity", "identity"),
    ];

    private static readonly string[] Layers = ["Core", "Application", "Infrastructure", "Api"];
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string[] ExpectedAssemblyNames { get; } =
    [
        .. Modules.SelectMany(module =>
            Layers.Select(layer => $"Travel.Modules.{module.Name}.{layer}")
        ),
    ];

    public static string[] ExpectedProjectPaths(string root) =>
        [
            .. Modules.SelectMany(module =>
                Layers.Select(layer =>
                    Path.GetFullPath(
                        Path.Combine(
                            root,
                            "modules",
                            module.Folder,
                            $"Travel.Modules.{module.Name}.{layer}",
                            $"Travel.Modules.{module.Name}.{layer}.csproj"
                        )
                    )
                )
            ),
        ];

    public static string[] FindProjectInventoryViolations(
        string root,
        IEnumerable<string> actualPaths
    )
    {
        var expected = ExpectedProjectPaths(root).ToHashSet(PathComparer);
        var actual = actualPaths.Select(Path.GetFullPath).ToHashSet(PathComparer);
        return expected
            .Except(actual, PathComparer)
            .Select(path => $"missing module project: {path}")
            .Concat(
                actual
                    .Except(expected, PathComparer)
                    .Select(path => $"unexpected module project: {path}")
            )
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] FindAssemblyInventoryViolations(IEnumerable<string?> assemblyNames)
    {
        var expected = ExpectedAssemblyNames.ToHashSet(StringComparer.Ordinal);
        var values = assemblyNames.ToArray();
        var actual = values.OfType<string>().ToHashSet(StringComparer.Ordinal);
        return expected
            .Except(actual, StringComparer.Ordinal)
            .Select(name => $"missing module assembly: {name}")
            .Concat(
                actual
                    .Except(expected, StringComparer.Ordinal)
                    .Select(name => $"unexpected module assembly: {name}")
            )
            .Concat(values.Any(string.IsNullOrWhiteSpace) ? ["invalid module assembly name"] : [])
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] FindCrossModuleProjectReferenceViolations(
        string root,
        string sourceProject,
        IEnumerable<string> references
    )
    {
        var sourceModule = ModuleFromProject(root, sourceProject);
        if (sourceModule is null)
            return [$"source is not a module project: {sourceProject}"];

        return references
            .Select(reference => (Path: reference, Module: ModuleFromProject(root, reference)))
            .Where(target =>
                target.Module is not null
                && !string.Equals(target.Module, sourceModule, StringComparison.OrdinalIgnoreCase)
            )
            .Select(target =>
                $"{Path.GetFileNameWithoutExtension(sourceProject)} -> {Path.GetFileNameWithoutExtension(target.Path)} ({sourceModule} -> {target.Module})"
            )
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] FindCrossModuleTypeDependencyViolations(IEnumerable<IType> sourceTypes)
    {
        var violations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sourceTypes)
        {
            var assemblyModule = ModuleFromAssembly(source.Assembly.Name);
            var namespaceModule = ModuleFromNamespace(source.Namespace?.FullName);
            var sourceModule = assemblyModule ?? namespaceModule;
            if (sourceModule is null)
                continue;
            if (assemblyModule is not null && namespaceModule != assemblyModule)
                violations.Add(
                    $"{source.FullName}: module namespace does not match assembly {source.Assembly.Name}"
                );

            foreach (var dependency in source.Dependencies)
            {
                var targetAssemblyModule = ModuleFromAssembly(dependency.Target.Assembly.Name);
                var targetNamespaceModule = ModuleFromNamespace(
                    dependency.Target.Namespace?.FullName
                );
                var targetModule = targetAssemblyModule ?? targetNamespaceModule;
                if (targetModule is null)
                    continue;
                if (
                    targetAssemblyModule is not null
                    && targetNamespaceModule != targetAssemblyModule
                )
                    violations.Add(
                        $"{source.FullName} -> {dependency.Target.FullName}: target module namespace does not match assembly {dependency.Target.Assembly.Name}"
                    );
                if (targetModule != sourceModule)
                    violations.Add(
                        $"{source.FullName} -> {dependency.Target.FullName} ({sourceModule} -> {targetModule})"
                    );
            }
        }

        return violations.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    public static string[] FindCrossModuleIlViolations(IEnumerable<IlTypeEdge> edges) =>
        [
            .. edges
                .Select(edge =>
                    (
                        Edge: edge,
                        Origin: ModuleFromAssembly(edge.OriginAssembly)
                            ?? ModuleFromNamespace(edge.OriginNamespace),
                        Target: ModuleFromAssembly(edge.TargetAssembly)
                            ?? ModuleFromNamespace(edge.TargetNamespace)
                    )
                )
                .Where(item =>
                    item.Origin is not null && item.Target is not null && item.Origin != item.Target
                )
                .Select(item =>
                    $"{item.Edge.OriginType} -> {item.Edge.TargetType} ({item.Origin} -> {item.Target})"
                )
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindImplementedSelectorViolations(IEnumerable<IType> sourceTypes)
    {
        var types = sourceTypes.ToArray();
        var required = new[]
        {
            ("Travel.Modules.Flights.Core", "Travel.Modules.Flights.Core"),
            ("Travel.Modules.Flights.Application", "Travel.Modules.Flights.Application"),
            ("Travel.Modules.Flights.Infrastructure", "Travel.Modules.Flights.Infrastructure"),
            ("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api"),
            ("Travel.Modules.Identity.Infrastructure", "Travel.Modules.Identity.Infrastructure"),
            ("Travel.Modules.Identity.Api", "Travel.Modules.Identity.Api.Composition"),
        };
        return required
            .Where(item =>
                !types.Any(type =>
                    type.Assembly.Name == item.Item1
                    && IsNamespaceWithin(type.Namespace?.FullName, item.Item2)
                    && !type.FullName.EndsWith("ModuleMarker", StringComparison.Ordinal)
                )
            )
            .Select(item => $"implemented selector is empty: {item.Item1} / {item.Item2}")
            .ToArray();
    }

    private static string? ModuleFromProject(string root, string projectPath)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(projectPath));
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (
            parts.Length < 3
            || !string.Equals(parts[0], "modules", StringComparison.OrdinalIgnoreCase)
        )
            return null;
        return Modules
                .FirstOrDefault(module =>
                    string.Equals(module.Folder, parts[1], StringComparison.OrdinalIgnoreCase)
                )
                ?.Name
            ?? parts[1];
    }

    private static string? ModuleFromAssembly(string? assemblyName)
    {
        if (
            assemblyName is null
            || !assemblyName.StartsWith("Travel.Modules.", StringComparison.Ordinal)
        )
            return null;
        var parts = assemblyName.Split('.');
        return parts.Length >= 4 ? parts[2] : null;
    }

    private static string? ModuleFromNamespace(string? name)
    {
        if (name is null || !name.StartsWith("Travel.Modules.", StringComparison.Ordinal))
            return null;
        var parts = name.Split('.');
        return parts.Length >= 4 ? parts[2] : null;
    }

    private static bool IsNamespaceWithin(string? name, string root) =>
        name is not null && (name == root || name.StartsWith(root + ".", StringComparison.Ordinal));
}
