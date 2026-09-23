using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Travel.Tests.Architecture.Support;

internal sealed record IlTypeEdge(
    string OriginAssembly,
    string OriginNamespace,
    string OriginType,
    string TargetAssembly,
    string TargetNamespace,
    string TargetType
);

internal static class IlTypeReferenceScanner
{
    public static IlTypeEdge[] Scan(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException(
                "Production assembly is missing for IL inspection.",
                assemblyPath
            );

        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters { InMemory = true, ReadSymbols = false }
        );
        var edges = new HashSet<IlTypeEdge>();
        foreach (var type in assembly.MainModule.Types)
            ScanType(type, assembly.Name.Name, edges);

        return edges
            .OrderBy(edge => edge.OriginType, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetAssembly, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetType, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ScanType(
        TypeDefinition origin,
        string originAssembly,
        HashSet<IlTypeEdge> edges
    )
    {
        Add(origin.BaseType);
        foreach (var item in origin.Interfaces)
            Add(item.InterfaceType);
        foreach (var field in origin.Fields)
            Add(field.FieldType);
        foreach (var property in origin.Properties)
            Add(property.PropertyType);
        foreach (var eventDefinition in origin.Events)
            Add(eventDefinition.EventType);
        foreach (var generic in origin.GenericParameters)
        foreach (var constraint in generic.Constraints)
            Add(constraint.ConstraintType);

        foreach (var method in origin.Methods)
        {
            Add(method.ReturnType);
            foreach (var parameter in method.Parameters)
                Add(parameter.ParameterType);
            foreach (var generic in method.GenericParameters)
            foreach (var constraint in generic.Constraints)
                Add(constraint.ConstraintType);

            if (!method.HasBody)
                continue;
            foreach (var variable in method.Body.Variables)
                Add(variable.VariableType);
            foreach (var handler in method.Body.ExceptionHandlers)
                Add(handler.CatchType);
            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case TypeReference type:
                        Add(type);
                        break;
                    case MethodReference reference:
                        Add(reference.DeclaringType);
                        Add(reference.ReturnType);
                        foreach (var parameter in reference.Parameters)
                            Add(parameter.ParameterType);
                        if (reference is GenericInstanceMethod genericMethod)
                            foreach (var argument in genericMethod.GenericArguments)
                                Add(argument);
                        break;
                    case FieldReference reference:
                        Add(reference.DeclaringType);
                        Add(reference.FieldType);
                        break;
                    case CallSite callSite:
                        Add(callSite.ReturnType);
                        foreach (var parameter in callSite.Parameters)
                            Add(parameter.ParameterType);
                        break;
                }
            }
        }

        foreach (var nested in origin.NestedTypes)
            ScanType(nested, originAssembly, edges);

        void Add(TypeReference? reference)
        {
            if (reference is null || reference is GenericParameter)
                return;
            if (reference is TypeSpecification specification)
            {
                Add(specification.ElementType);
                if (specification is GenericInstanceType genericInstance)
                    foreach (var argument in genericInstance.GenericArguments)
                        Add(argument);
                if (specification is IModifierType modifier)
                    Add(modifier.ModifierType);
                return;
            }

            var targetAssembly = reference.Scope switch
            {
                AssemblyNameReference external => external.Name,
                ModuleDefinition local => local.Assembly.Name.Name,
                _ => reference.Module?.Assembly?.Name.Name,
            };
            if (
                targetAssembly is null
                || !targetAssembly.StartsWith("Travel.", StringComparison.Ordinal)
            )
                return;
            var originNamespace = OriginNamespace(origin);
            edges.Add(
                new IlTypeEdge(
                    originAssembly,
                    originNamespace,
                    origin.FullName,
                    targetAssembly,
                    reference.Namespace ?? string.Empty,
                    reference.FullName
                )
            );
        }
    }

    private static string OriginNamespace(TypeDefinition type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            if (!string.IsNullOrEmpty(current.Namespace))
                return current.Namespace;
        return string.Empty;
    }
}
