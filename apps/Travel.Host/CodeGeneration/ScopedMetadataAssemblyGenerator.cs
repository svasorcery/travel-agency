using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.RuntimeCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Travel.Host.CodeGeneration;

// Uses the existing JasperFx compiler contract with explicitly owned metadata:
// each Emit owns its metadata; emitted assemblies retain the upstream load-context lifetime.
[RequiresDynamicCode("Compiles generated C# at runtime through Roslyn.")]
[RequiresUnreferencedCode("Resolves generated-code references and loads runtime-generated types.")]
internal sealed class ScopedMetadataAssemblyGenerator : IAssemblyGenerator
{
    private readonly object gate = new();
    private readonly HashSet<Assembly> roots =
    [
        typeof(object).Assembly,
        typeof(Enumerable).Assembly,
    ];
    private readonly Func<string, AssemblyMetadata> open;
    private string? assemblyName;

    public ScopedMetadataAssemblyGenerator()
        : this(AssemblyMetadata.CreateFromFile) { }

    internal ScopedMetadataAssemblyGenerator(Func<string, AssemblyMetadata> open)
    {
        this.open = open;
    }

    public string? AssemblyName
    {
        get
        {
            lock (gate)
                return assemblyName;
        }
        set
        {
            lock (gate)
                assemblyName = value;
        }
    }

    public void ReferenceAssembly(Assembly assembly)
    {
        if (assembly is null || assembly.IsDynamic)
            return;
        lock (gate)
            roots.Add(assembly);
    }

    public void ReferenceAssemblyContainingType<T>() => ReferenceAssembly(typeof(T).Assembly);

    public Assembly Generate(Action<ISourceWriter> write)
    {
        using var writer = new SourceWriter();
        write(writer);
        return Generate(writer.Code());
    }

    public Assembly Generate(string code)
    {
        Assembly[] references;
        string? name;
        lock (gate)
        {
            references = roots.ToArray();
            name = assemblyName;
        }
        return Emit(code, references, name);
    }

    public void Compile(
        GeneratedAssembly generatedAssembly,
        IServiceVariableSource? services = null
    ) => Compile(generatedAssembly, services, out _);

    public void Compile(
        GeneratedAssembly generatedAssembly,
        IServiceVariableSource? services,
        out string code
    )
    {
        code = generatedAssembly.GenerateCode(services);
        Assembly[] references;
        lock (gate)
            references = roots.ToArray();
        // Preserve the upstream Compile reference inputs; don't add Travel.Host as a new root.
        var compilationRoots = references
            .Concat([typeof(AssemblyGenerator).Assembly, typeof(Task).Assembly])
            .Concat(generatedAssembly.Rules.Assemblies)
            .Concat(generatedAssembly.AllReferencedAssemblies())
            .Concat(generatedAssembly.GeneratedTypes.SelectMany(t => t.AssemblyReferences()));
        var assembly = Emit(code, compilationRoots, name: null);
        generatedAssembly.AttachAssembly(assembly);
    }

    private Assembly Emit(string code, IEnumerable<Assembly> compilationRoots, string? name)
    {
        using var metadata = new CompilationMetadata(open);
        foreach (var root in compilationRoots)
            metadata.Reference(root);
        var compilation = CSharpCompilation.Create(
            name ?? Path.GetRandomFileName(),
            [CSharpSyntaxTree.ParseText(code)],
            metadata.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var failures = result.Diagnostics.Where(d =>
                d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error
            );
            throw new InvalidOperationException(
                "Compilation failures!\n\n"
                    + string.Join("\n", failures.Select(d => $"{d.Id}: {d.GetMessage()}"))
                    + "\n\nCode:\n\n"
                    + code
            );
        }
        stream.Position = 0;
        using var context = new CustomAssemblyLoadContext();
        return context.LoadFromStream(stream);
        // Disposes owned PE images/streams on success, compiler error, reference error or load error.
    }

    private sealed class CompilationMetadata(Func<string, AssemblyMetadata> open) : IDisposable
    {
        private readonly HashSet<Assembly> visited = [];
        private readonly HashSet<string> paths = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
        );
        private readonly List<AssemblyMetadata> owners = [];
        internal List<MetadataReference> References { get; } = [];

        internal void Reference(Assembly assembly)
        {
            if (!visited.Add(assembly) || assembly.IsDynamic)
                return;
            var path = ReferencePath(assembly);
            if (path is null)
                return;
            path = Path.GetFullPath(path);
            if (paths.Add(path))
            {
                var owner = open(path);
                owners.Add(owner); // Ownership is recorded before GetReference can fail.
                References.Add(owner.GetReference(filePath: path));
            }

            foreach (var name in assembly.GetReferencedAssemblies())
            {
                Assembly dependency;
                try
                {
                    dependency = Assembly.Load(name);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }
                catch (FileLoadException)
                {
                    continue;
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
                // Like upstream, optional platform dependencies may be absent.
                // File/metadata failures for a resolved assembly remain explicit compilation failures.
                Reference(dependency);
            }
        }

        private static string? ReferencePath(Assembly assembly)
        {
            if (!string.IsNullOrEmpty(assembly.Location))
                return assembly.Location;
            // Retain the existing JasperFx single-file hint-path contract; no new resolver/cache.
            if (AssemblyGenerator.HintPaths is not { } hints)
                return null;
            foreach (var hint in hints)
            {
                var path = Directory
                    .EnumerateFiles(
                        hint,
                        assembly.GetName().Name + ".dll",
                        SearchOption.AllDirectories
                    )
                    .FirstOrDefault();
                if (path is not null)
                    return path;
            }
            return null;
        }

        public void Dispose()
        {
            List<Exception>? errors = null;
            foreach (var owner in owners)
            {
                try
                {
                    owner.Dispose();
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }
            owners.Clear();
            References.Clear();
            if (errors is not null)
                throw new AggregateException("Metadata cleanup failed.", errors);
        }
    }
}
