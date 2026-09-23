using ArchUnitNET.Domain;
using Travel.Shared.Abstractions;

namespace Travel.Tests.Architecture.Support;

internal static class LayerArchitecturePolicy
{
    private sealed record ModuleLayer(string Module, string Layer);

    public static bool IsForbiddenLayerEdge(string source, string target) =>
        source switch
        {
            "Core" => target is "Application" or "Infrastructure" or "Api",
            "Application" => target is "Infrastructure" or "Api",
            "Infrastructure" => target is "Api",
            _ => false,
        };

    public static string[] FindProjectReferenceViolations(
        string root,
        string sourceProject,
        IEnumerable<string> references
    )
    {
        var source = ParseModuleProject(sourceProject);
        if (source is null)
            return [$"source is not a module layer project: {sourceProject}"];

        var errors = new List<string>();
        foreach (var reference in references)
        {
            var target = ParseModuleProject(reference);
            if (target is not null && target.Module == source.Module)
            {
                if (IsForbiddenLayerEdge(source.Layer, target.Layer))
                    errors.Add(
                        $"{Path.GetFileNameWithoutExtension(sourceProject)} -> {Path.GetFileNameWithoutExtension(reference)}"
                    );
                if (
                    source.Layer == "Api"
                    && target.Layer == "Infrastructure"
                    && source.Module is not ("Flights" or "Identity")
                )
                    errors.Add($"scaffold Api -> Infrastructure: {sourceProject} -> {reference}");
            }

            if (
                source.Layer is "Core" or "Application"
                && Path.GetFileNameWithoutExtension(reference) == "Travel.Shared.Web"
            )
                errors.Add($"{sourceProject} -> Travel.Shared.Web");
        }

        return errors
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static string[] FindLayerTypeViolations(IEnumerable<IType> origins) =>
        [
            .. origins
                .SelectMany(origin =>
                {
                    var source = ParseModuleType(origin);
                    if (source is null)
                        return [];
                    return origin
                        .Dependencies.Select(dependency =>
                            (Dependency: dependency, Target: ParseModuleType(dependency.Target))
                        )
                        .Where(pair =>
                            pair.Target is not null
                            && pair.Target.Module == source.Module
                            && IsForbiddenLayerEdge(source.Layer, pair.Target.Layer)
                        )
                        .Select(pair =>
                            $"{origin.FullName} -> {pair.Dependency.Target.FullName} ({source.Module}.{source.Layer} -> {pair.Target!.Module}.{pair.Target.Layer})"
                        );
                })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindApiInfrastructureTypeViolations(IEnumerable<IType> origins) =>
        [
            .. origins
                .Where(origin =>
                {
                    var source = ParseModuleType(origin);
                    return source?.Layer == "Api"
                        && !IsCompositionNamespace(origin.Namespace?.FullName, source.Module);
                })
                .SelectMany(origin =>
                    origin
                        .Dependencies.Where(dependency =>
                            ParseModuleType(dependency.Target)?.Layer == "Infrastructure"
                        )
                        .Select(dependency => $"{origin.FullName} -> {dependency.Target.FullName}")
                )
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindContractConsumerTypeViolations(IEnumerable<IType> origins) =>
        [
            .. origins
                .Where(origin => !IsApprovedContractOrigin(origin))
                .SelectMany(origin =>
                    origin
                        .Dependencies.Where(dependency =>
                            dependency.Target.Assembly.Name == "Travel.IntegrationContracts.AI"
                        )
                        .Select(dependency => $"{origin.FullName} -> {dependency.Target.FullName}")
                )
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindLayerIlViolations(IEnumerable<IlTypeEdge> edges) =>
        [
            .. edges
                .Select(edge =>
                    (
                        Edge: edge,
                        Origin: ParseModuleEdgeOrigin(edge),
                        Target: ParseModuleAssemblyName(edge.TargetAssembly)
                    )
                )
                .Where(item =>
                    item.Origin is not null
                    && item.Target is not null
                    && item.Origin.Module == item.Target.Module
                    && IsForbiddenLayerEdge(item.Origin.Layer, item.Target.Layer)
                )
                .Select(item => $"{item.Edge.OriginType} -> {item.Edge.TargetType}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindApiInfrastructureIlViolations(IEnumerable<IlTypeEdge> edges) =>
        [
            .. edges
                .Where(edge =>
                {
                    var source = ParseModuleEdgeOrigin(edge);
                    var target = ParseModuleAssemblyName(edge.TargetAssembly);
                    return source?.Layer == "Api"
                        && target?.Layer == "Infrastructure"
                        && !IsCompositionNamespace(edge.OriginNamespace, source.Module);
                })
                .Select(edge => $"{edge.OriginType} -> {edge.TargetType}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindContractConsumerIlViolations(IEnumerable<IlTypeEdge> edges) =>
        [
            .. edges
                .Where(edge =>
                    edge.TargetAssembly == "Travel.IntegrationContracts.AI"
                    && !IsApprovedContractEdgeOrigin(edge)
                )
                .Select(edge => $"{edge.OriginType} -> {edge.TargetType}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal),
        ];

    public static string[] FindImplementedApiSelectorViolations(IEnumerable<IType> origins)
    {
        var types = origins.ToArray();
        var required = new[]
        {
            ("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Endpoints"),
            ("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Contracts"),
            ("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Middleware"),
            ("Travel.Modules.Flights.Api", "Travel.Modules.Flights.Api.Composition"),
            ("Travel.Modules.Identity.Api", "Travel.Modules.Identity.Api.Composition"),
        };
        return required
            .Where(row =>
                !types.Any(type =>
                    type.Assembly.Name == row.Item1 && IsWithin(type.Namespace?.FullName, row.Item2)
                )
            )
            .Select(row => $"implemented Api selector is empty: {row.Item1} / {row.Item2}")
            .ToArray();
    }

    public static string[] FindDomainEventViolations(IEnumerable<IType> origins)
    {
        var errors = new List<string>();
        foreach (var origin in origins)
        {
            var module = ParseModuleType(origin);
            if (module?.Layer != "Core")
                continue;

            var inEventNamespace = IsWithin(
                origin.Namespace?.FullName,
                $"Travel.Modules.{module.Module}.Core.DomainEvents"
            );
            var assembly = AppDomain
                .CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => candidate.GetName().Name == origin.Assembly.Name);
            var clrType = assembly?.GetType(origin.FullName);
            if (clrType is null)
            {
                if (inEventNamespace)
                    errors.Add($"cannot resolve domain event type: {origin.FullName}");
                continue;
            }
            if (clrType.IsEnum || clrType.IsInterface)
                continue;
            var implementsEvent = typeof(IDomainEvent).IsAssignableFrom(clrType);
            if (inEventNamespace && !implementsEvent)
                errors.Add($"domain event namespace type lacks IDomainEvent: {origin.FullName}");
            if (implementsEvent && !inEventNamespace)
                errors.Add($"IDomainEvent type is outside DomainEvents: {origin.FullName}");
        }
        return errors.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    public static string[] FindMissingRequiredDomainEvents(IEnumerable<IType> origins)
    {
        var names = origins.Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);
        return new[] { "OfferHeld", "OrderConfirmed" }
            .Select(name => $"Travel.Modules.Flights.Core.DomainEvents.{name}")
            .Where(name => !names.Contains(name))
            .Select(name => $"required Flights domain event selector is empty: {name}")
            .ToArray();
    }

    private static ModuleLayer? ParseModuleEdgeOrigin(IlTypeEdge edge)
    {
        var assembly = ParseModuleAssemblyName(edge.OriginAssembly);
        if (assembly is not null)
            return assembly;
        var parts = edge.OriginNamespace.Split('.');
        return parts.Length >= 4 && parts[0] == "Travel" && parts[1] == "Modules"
            ? new ModuleLayer(parts[2], parts[3])
            : null;
    }

    private static bool IsApprovedContractEdgeOrigin(IlTypeEdge edge) =>
        edge.OriginAssembly == "Travel.IntegrationContracts.AI"
        || (
            edge.OriginAssembly == "Travel.Modules.Flights.Application"
            && IsWithin(edge.OriginNamespace, "Travel.Modules.Flights.Application")
        )
        || (edge.OriginAssembly == "Travel.AI" && IsWithin(edge.OriginNamespace, "Travel.AI"))
        || (
            edge.OriginAssembly == "Travel.Modules.Flights.Api"
            && IsCompositionNamespace(edge.OriginNamespace, "Flights")
        );

    private static ModuleLayer? ParseModuleProject(string projectPath)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        return ParseModuleAssemblyName(name);
    }

    private static ModuleLayer? ParseModuleType(IType type)
    {
        var assembly = ParseModuleAssemblyName(type.Assembly.Name);
        if (assembly is not null)
            return assembly;

        var name = type.Namespace?.FullName;
        if (name is null)
            return null;
        var parts = name.Split('.');
        return parts.Length >= 4 && parts[0] == "Travel" && parts[1] == "Modules"
            ? new ModuleLayer(parts[2], parts[3])
            : null;
    }

    private static ModuleLayer? ParseModuleAssemblyName(string? name)
    {
        var parts = name?.Split('.');
        return parts is { Length: 4 } && parts[0] == "Travel" && parts[1] == "Modules"
            ? new ModuleLayer(parts[2], parts[3])
            : null;
    }

    private static bool IsApprovedContractOrigin(IType origin)
    {
        var assembly = origin.Assembly.Name;
        var name = origin.Namespace?.FullName;
        return assembly == "Travel.IntegrationContracts.AI"
            || (
                assembly == "Travel.Modules.Flights.Application"
                && IsWithin(name, "Travel.Modules.Flights.Application")
            )
            || (assembly == "Travel.AI" && IsWithin(name, "Travel.AI"))
            || (
                assembly == "Travel.Modules.Flights.Api" && IsCompositionNamespace(name, "Flights")
            );
    }

    private static bool IsCompositionNamespace(string? name, string module) =>
        IsWithin(name, $"Travel.Modules.{module}.Api.Composition");

    private static bool IsWithin(string? name, string root) =>
        name is not null && (name == root || name.StartsWith(root + ".", StringComparison.Ordinal));
}
