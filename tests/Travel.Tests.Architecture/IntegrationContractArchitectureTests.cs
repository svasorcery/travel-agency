using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Shouldly;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class IntegrationContractArchitectureTests
{
    private const string ContractProjectRelativePath =
        "shared/dotnet/Travel.IntegrationContracts.AI/Travel.IntegrationContracts.AI.csproj";
    private const string ContractAssemblyName = "Travel.IntegrationContracts.AI";
    private const string ContractSourceRelativePath =
        "shared/dotnet/Travel.IntegrationContracts.AI/NlSearch/NlSearchContracts.cs";
    private const int MaxMsBuildOutputCharacters = 2 * 1024 * 1024;
    private const int MaxRestoreGraphCharacters = 4 * 1024 * 1024;
    private static readonly TimeSpan MsBuildTimeout = TimeSpan.FromSeconds(20);
    private static readonly string[] Configurations = ["Debug", "Release"];
    private static readonly string[] DependencyItemNames =
    [
        "PackageReference",
        "ProjectReference",
        "FrameworkReference",
        "Reference",
        "COMReference",
        "NativeReference",
    ];

    private static readonly HashSet<string> IgnoredDirectoryNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".git",
        ".nx",
        "bin",
        "node_modules",
        "obj",
        "TestResults",
    };

    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string RootDirectoryBuildProps = Path.GetFullPath(
        Path.Combine(RepositoryRoot, "Directory.Build.props")
    );
    private static readonly SemaphoreSlim MsBuildProcessSlots = new(
        Math.Clamp(Environment.ProcessorCount / 4, 1, 2)
    );
    private static readonly ConcurrentDictionary<
        EvaluationKey,
        Lazy<Task<EvaluatedProject>>
    > EvaluationCache = new();
    private static readonly ConcurrentDictionary<
        EvaluationKey,
        Lazy<Task<EvaluatedRestoreGraph>>
    > RestoreGraphCache = new();
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    [Fact]
    public void Contract_project_and_transitive_runtime_assembly_are_present()
    {
        File.Exists(Path.Combine(RepositoryRoot, ContractProjectRelativePath))
            .ShouldBeTrue("the shared NL-search contract project must exist");
        File.Exists(Path.Combine(AppContext.BaseDirectory, $"{ContractAssemblyName}.dll"))
            .ShouldBeTrue(
                "the Architecture test output must receive the contract through Flights Application, without a direct Architecture project reference"
            );
    }

    [Fact]
    public void Contract_runtime_assembly_references_match_the_leaf_allowlist()
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{ContractAssemblyName}.dll");
        File.Exists(assemblyPath)
            .ShouldBeTrue("the contract DLL is required for dependency inspection");

        var references = Assembly
            .LoadFrom(assemblyPath)
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();

        FindRuntimeReferenceViolations(references).ShouldBeEmpty();
    }

    [Fact]
    public async Task Contract_project_declares_only_the_leaf_dependency_allowlist()
    {
        var projectPath = Path.GetFullPath(
            Path.Combine(RepositoryRoot, ContractProjectRelativePath)
        );

        var evaluations = await Task.WhenAll(
            Configurations.Select(configuration => EvaluateProjectAsync(projectPath, configuration))
        );

        foreach (var evaluation in evaluations)
            FindProjectDependencyViolations(projectPath, evaluation).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Microsoft.Extensions.Logging.Abstractions")]
    [InlineData("Travel.Shared.Domain")]
    public void Leaf_dependency_validator_rejects_unexpected_runtime_assemblies(string assemblyName)
    {
        FindRuntimeReferenceViolations(["System.Runtime", "Wolverine", assemblyName])
            .ShouldContain($"runtime assembly: {assemblyName}");
    }

    [Fact]
    public void Leaf_dependency_validator_requires_the_exact_Wolverine_runtime_assembly()
    {
        FindRuntimeReferenceViolations(["System.Runtime"])
            .ShouldContain("runtime assembly: expected exactly one Wolverine; found 0");
        FindRuntimeReferenceViolations(["System.Runtime", "WolverineFx"])
            .ShouldContain("runtime assembly: WolverineFx");
    }

    [Theory]
    [InlineData("ExplicitForbiddenPackage.proj")]
    [InlineData("ImportedForbiddenPackage.proj")]
    [InlineData("ExplicitForbiddenProjectReference.proj")]
    [InlineData("PropertyImportedProjectReference.proj")]
    [InlineData("ExplicitForbiddenFrameworkReference.proj")]
    [InlineData("ImportedForbiddenFrameworkReference.proj")]
    [InlineData("ForbiddenReference.proj")]
    [InlineData("MissingWolverine.proj")]
    [InlineData("DuplicateWolverine.proj")]
    [InlineData("ImportedWolverine.proj")]
    public async Task Evaluated_leaf_dependency_validator_rejects_forbidden_items(
        string fixtureName
    )
    {
        var fixturePath = GetLeafDependencyGuardFixturePath(fixtureName);

        var evaluation = await EvaluateProjectAsync(fixturePath, "Debug");

        FindProjectDependencyViolations(fixturePath, evaluation).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Evaluated_leaf_dependency_validator_fails_closed_when_MSBuild_evaluation_fails()
    {
        var fixturePath = GetLeafDependencyGuardFixturePath("MissingImport.proj");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            EvaluateProjectAsync(fixturePath, "Debug")
        );

        exception.Message.ShouldContain("DoesNotExist.props");
    }

    [Fact]
    public async Task Evaluated_direct_consumer_scan_detects_property_imported_contract_reference()
    {
        var contractProjectPath = Path.GetFullPath(
            Path.Combine(RepositoryRoot, ContractProjectRelativePath)
        );
        var fixturePath = GetLeafDependencyGuardFixturePath("DirectConsumerPropertyImported.proj");

        var consumers = await FindDirectProjectConsumersAsync(
            contractProjectPath,
            [fixturePath],
            ["Debug"]
        );

        consumers.ShouldContain(fixturePath);
    }

    [Fact]
    public void Required_direct_consumer_must_exist_in_Release_not_only_Debug()
    {
        var travelAiProject = Path.GetFullPath(
            Path.Combine(RepositoryRoot, "apps/Travel.AI/Travel.AI.csproj")
        );

        FindDirectConsumerSetViolations([travelAiProject], [travelAiProject], "Debug")
            .ShouldBeEmpty();
        FindDirectConsumerSetViolations([], [travelAiProject], "Release")
            .ShouldContain(violation => violation.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Additional_direct_consumer_is_not_pre_authorized()
    {
        var expectedConsumers = new[]
        {
            "modules/flights/Travel.Modules.Flights.Application/Travel.Modules.Flights.Application.csproj",
            "apps/Travel.AI/Travel.AI.csproj",
            "tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj",
        }
            .Select(path => Path.GetFullPath(Path.Combine(RepositoryRoot, path)))
            .ToArray();
        var flightsApiProject = Path.GetFullPath(
            Path.Combine(
                RepositoryRoot,
                "modules/flights/Travel.Modules.Flights.Api/Travel.Modules.Flights.Api.csproj"
            )
        );

        FindDirectConsumerSetViolations(
                [.. expectedConsumers, flightsApiProject],
                expectedConsumers,
                "Debug"
            )
            .ShouldContain(violation => violation.Contains("unexpected", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_graph_parser_fails_closed_when_an_enumerated_project_is_missing()
    {
        var representedProject = Path.GetFullPath(
            Path.Combine(RepositoryRoot, ContractProjectRelativePath)
        );
        var missingProject = Path.GetFullPath(
            Path.Combine(
                RepositoryRoot,
                "shared/dotnet/Travel.Shared.Domain/Travel.Shared.Domain.csproj"
            )
        );
        var graphJson = JsonSerializer.Serialize(
            new
            {
                format = 1,
                projects = new Dictionary<string, object>
                {
                    [representedProject] = new
                    {
                        restore = new
                        {
                            projectPath = representedProject,
                            frameworks = new Dictionary<string, object>(),
                        },
                    },
                },
            }
        );

        var exception = Should.Throw<InvalidOperationException>(() =>
            ParseRestoreGraph(
                graphJson,
                new EvaluationKey("fixture.proj", "Debug"),
                [representedProject, missingProject]
            )
        );

        exception.Message.ShouldContain(missingProject);
    }

    [Fact]
    public void Restore_graph_parser_fails_closed_when_expected_project_has_no_framework()
    {
        var projectPath = Path.GetFullPath(
            Path.Combine(RepositoryRoot, ContractProjectRelativePath)
        );
        var graphJson = JsonSerializer.Serialize(
            new
            {
                format = 1,
                projects = new Dictionary<string, object>
                {
                    [projectPath] = new
                    {
                        restore = new
                        {
                            projectPath,
                            frameworks = new Dictionary<string, object>(),
                        },
                    },
                },
            }
        );

        Should.Throw<InvalidOperationException>(() =>
            ParseRestoreGraph(graphJson, new EvaluationKey("fixture.proj", "Debug"), [projectPath])
        );
    }

    [Fact]
    public async Task Only_approved_projects_directly_reference_the_contract_and_required_consumers_do()
    {
        var contractProjectPath = Path.GetFullPath(
            Path.Combine(RepositoryRoot, ContractProjectRelativePath)
        );
        File.Exists(contractProjectPath)
            .ShouldBeTrue("the contract project is required for MSBuild graph inspection");

        var approved = new[]
        {
            "modules/flights/Travel.Modules.Flights.Application/Travel.Modules.Flights.Application.csproj",
            "apps/Travel.AI/Travel.AI.csproj",
            "tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj",
        }
            .Select(path => Path.GetFullPath(Path.Combine(RepositoryRoot, path)))
            .ToArray();

        foreach (var configuration in Configurations)
        {
            var consumers = await FindDirectProjectConsumersAsync(
                contractProjectPath,
                configurations: [configuration]
            );

            FindDirectConsumerSetViolations(consumers, approved, configuration).ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("NlSearchRequested")]
    [InlineData("NlSearchParsed")]
    public void Each_nl_search_message_has_one_production_declaration_in_the_contract_project(
        string messageName
    )
    {
        var sourceFiles = EnumerateFiles("*.cs", RepositoryRoot)
            .Where(path =>
                !Path.GetRelativePath(RepositoryRoot, path)
                    .StartsWith(
                        $"tests{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .ToArray();

        var declarations = sourceFiles
            .Where(path => DeclaresClrType(File.ReadAllText(path), messageName))
            .ToArray();

        declarations.Length.ShouldBe(
            1,
            $"{messageName} must have exactly one production CLR declaration"
        );
        Path.GetFullPath(declarations[0])
            .ShouldBe(Path.GetFullPath(Path.Combine(RepositoryRoot, ContractSourceRelativePath)));
    }

    [Theory]
    [InlineData("public sealed class NlSearchRequested { }", "NlSearchRequested")]
    [InlineData("internal readonly struct NlSearchParsed { }", "NlSearchParsed")]
    [InlineData("public sealed record class NlSearchRequested(string Query);", "NlSearchRequested")]
    [InlineData(
        "internal readonly record struct NlSearchParsed(Guid CorrelationId);",
        "NlSearchParsed"
    )]
    [InlineData("file static partial class NlSearchRequested { }", "NlSearchRequested")]
    [InlineData("public readonly ref struct NlSearchParsed { }", "NlSearchParsed")]
    [InlineData(
        "internal abstract partial record class NlSearchRequested { }",
        "NlSearchRequested"
    )]
    [InlineData("public unsafe partial struct NlSearchParsed { }", "NlSearchParsed")]
    [InlineData("public interface NlSearchRequested { }", "NlSearchRequested")]
    [InlineData("internal enum NlSearchParsed : byte { Unknown }", "NlSearchParsed")]
    [InlineData(
        "public delegate ValueTask<NlSearchParsed?> NlSearchRequested<TRequest>(TRequest request, CancellationToken cancellationToken) where TRequest : class;",
        "NlSearchRequested"
    )]
    [InlineData("[Obsolete] public sealed class NlSearchRequested { }", "NlSearchRequested")]
    [InlineData(
        "[MessageIdentity(\"travel.ai.nl-search.parsed\", Version = 1)] public sealed record NlSearchParsed(Guid CorrelationId);",
        "NlSearchParsed"
    )]
    [InlineData(
        "[Obsolete, CLSCompliant(false)] internal partial interface @NlSearchRequested<in TQuery> where TQuery : notnull { }",
        "NlSearchRequested"
    )]
    [InlineData("private protected enum NlSearchParsed : short { Unknown }", "NlSearchParsed")]
    public void Production_declaration_detector_recognizes_all_supported_clr_type_forms(
        string source,
        string messageName
    )
    {
        DeclaresClrType(source, messageName).ShouldBeTrue();
    }

    [Theory]
    [InlineData("var request = new NlSearchRequested(query, correlationId);")]
    [InlineData("public NlSearchRequested(string query) { }")]
    [InlineData("public NlSearchParsed NlSearchParsed(NlSearchRequested request) => default!;")]
    [InlineData("public NlSearchRequested Create(NlSearchParsed parsed) => default!;")]
    [InlineData("[Obsolete] public NlSearchParsed Handle(NlSearchRequested request) => default!;")]
    public void Production_declaration_detector_ignores_usages_constructors_and_methods(
        string source
    )
    {
        DeclaresClrType(source, "NlSearchRequested").ShouldBeFalse();
        DeclaresClrType(source, "NlSearchParsed").ShouldBeFalse();
    }

    private static async Task<string[]> FindDirectProjectConsumersAsync(
        string contractProjectPath,
        IEnumerable<string>? projectPaths = null,
        IEnumerable<string>? configurations = null
    )
    {
        var allCandidates = (projectPaths ?? EnumerateFiles("*.csproj", RepositoryRoot))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToArray();
        var candidates = allCandidates
            .Where(path => !PathsEqual(path, contractProjectPath))
            .ToArray();
        var entryProjectPath = projectPaths is null
            ? Path.Combine(RepositoryRoot, "Travel.slnx")
            : candidates.ShouldHaveSingleItem();
        var expectedProjects = projectPaths is null ? allCandidates : null;
        var graphs = new List<EvaluatedRestoreGraph>();
        foreach (var configuration in configurations ?? Configurations)
        {
            graphs.Add(
                await EvaluateRestoreGraphAsync(entryProjectPath, configuration, expectedProjects)
            );
        }

        return graphs
            .SelectMany(graph =>
                candidates.Where(projectPath =>
                    graph.ProjectReferences.TryGetValue(projectPath, out var references)
                    && references.Any(reference => PathsEqual(reference, contractProjectPath))
                )
            )
            .Distinct(PathComparer)
            .ToArray();
    }

    private static string GetLeafDependencyGuardFixturePath(string fixtureName) =>
        Path.GetFullPath(
            Path.Combine(
                RepositoryRoot,
                "tests",
                "Travel.Tests.Architecture",
                "Fixtures",
                "LeafDependencyGuard",
                fixtureName
            )
        );

    private static string[] FindDirectConsumerSetViolations(
        IEnumerable<string> consumers,
        IEnumerable<string> expectedConsumers,
        string configuration
    )
    {
        var actual = consumers.Select(Path.GetFullPath).ToHashSet(PathComparer);
        var expected = expectedConsumers.Select(Path.GetFullPath).ToHashSet(PathComparer);
        return expected
            .Except(actual, PathComparer)
            .Select(path => $"{configuration} direct consumer missing: {path}")
            .Concat(
                actual
                    .Except(expected, PathComparer)
                    .Select(path => $"{configuration} direct consumer unexpected: {path}")
            )
            .ToArray();
    }

    private static string[] FindRuntimeReferenceViolations(IEnumerable<string> references)
    {
        var referenceNames = references.ToArray();
        var violations = referenceNames
            .Where(name =>
                !name.StartsWith("System.", StringComparison.Ordinal)
                && !name.Equals("Wolverine", StringComparison.Ordinal)
            )
            .Select(name => $"runtime assembly: {name}")
            .ToList();
        var wolverineReferenceCount = referenceNames.Count(name =>
            name.Equals("Wolverine", StringComparison.Ordinal)
        );

        if (wolverineReferenceCount != 1)
        {
            violations.Add(
                $"runtime assembly: expected exactly one Wolverine; found {wolverineReferenceCount}"
            );
        }

        return [.. violations];
    }

    private static string[] FindProjectDependencyViolations(
        string projectPath,
        EvaluatedProject project
    )
    {
        var violations = new List<string>();
        var packages = project.Items["PackageReference"];
        var wolverinePackages = packages
            .Where(package => package.Identity.Equals("WolverineFx", StringComparison.Ordinal))
            .ToArray();
        var roslynatorPackages = packages
            .Where(package =>
                package.Identity.Equals("Roslynator.Analyzers", StringComparison.Ordinal)
            )
            .ToArray();

        violations.AddRange(
            packages
                .Where(package =>
                    !package.Identity.Equals("WolverineFx", StringComparison.Ordinal)
                    && !package.Identity.Equals("Roslynator.Analyzers", StringComparison.Ordinal)
                )
                .Select(package => $"PackageReference: {package.Identity}")
        );

        if (wolverinePackages.Length != 1)
        {
            violations.Add(
                $"PackageReference: expected exactly one direct WolverineFx; found {wolverinePackages.Length}"
            );
        }
        else if (!PathsEqual(wolverinePackages[0].DefiningProjectFullPath, projectPath))
        {
            violations.Add(
                $"PackageReference: WolverineFx must be defined directly by {projectPath}; found {wolverinePackages[0].DefiningProjectFullPath}"
            );
        }

        if (roslynatorPackages.Length != 1)
        {
            violations.Add(
                $"PackageReference: expected exactly one imported Roslynator.Analyzers; found {roslynatorPackages.Length}"
            );
        }
        else
        {
            var roslynator = roslynatorPackages[0];
            if (!PathsEqual(roslynator.DefiningProjectFullPath, RootDirectoryBuildProps))
            {
                violations.Add(
                    $"PackageReference: Roslynator.Analyzers must be defined by {RootDirectoryBuildProps}; found {roslynator.DefiningProjectFullPath}"
                );
            }

            if (
                !roslynator.Metadata.TryGetValue("PrivateAssets", out var privateAssets)
                || !privateAssets.Equals("all", StringComparison.OrdinalIgnoreCase)
            )
            {
                violations.Add(
                    $"PackageReference: Roslynator.Analyzers PrivateAssets must be all; found {privateAssets ?? "<missing>"}"
                );
            }
        }

        foreach (
            var itemName in new[]
            {
                "ProjectReference",
                "Reference",
                "COMReference",
                "NativeReference",
            }
        )
        {
            violations.AddRange(
                project.Items[itemName].Select(item => $"{itemName}: {item.Identity}")
            );
        }

        var frameworks = project.Items["FrameworkReference"];
        if (
            frameworks.Length != 1
            || !frameworks[0].Identity.Equals("Microsoft.NETCore.App", StringComparison.Ordinal)
        )
        {
            violations.Add(
                $"FrameworkReference: expected exactly one implicit Microsoft.NETCore.App; found {(frameworks.Length == 0 ? "none" : string.Join(", ", frameworks.Select(item => item.Identity)))}"
            );
        }
        else
        {
            var framework = frameworks[0];
            if (
                !framework.Metadata.TryGetValue("IsImplicitlyDefined", out var isImplicit)
                || !isImplicit.Equals("true", StringComparison.OrdinalIgnoreCase)
            )
            {
                violations.Add(
                    $"FrameworkReference: Microsoft.NETCore.App must have IsImplicitlyDefined=true; found {isImplicit ?? "<missing>"}"
                );
            }

            if (IsWithinRepository(framework.DefiningProjectFullPath))
            {
                violations.Add(
                    $"FrameworkReference: Microsoft.NETCore.App must be defined outside the repository; found {framework.DefiningProjectFullPath}"
                );
            }
        }

        return [.. violations];
    }

    private static Task<EvaluatedProject> EvaluateProjectAsync(
        string projectPath,
        string configuration
    )
    {
        var key = new EvaluationKey(Path.GetFullPath(projectPath), configuration);
        return EvaluationCache
            .GetOrAdd(
                key,
                static key => new Lazy<Task<EvaluatedProject>>(
                    () => RunMsBuildEvaluationAsync(key),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;
    }

    private static async Task<EvaluatedProject> RunMsBuildEvaluationAsync(EvaluationKey key)
    {
        if (!File.Exists(key.ProjectPath))
            throw new InvalidOperationException(
                $"MSBuild evaluation project does not exist: {key.ProjectPath}"
            );

        var startInfo = CreateMsBuildStartInfo(key);
        startInfo.ArgumentList.Add($"-getItem:{string.Join(',', DependencyItemNames)}");
        var result = await RunProcessAsync(
            startInfo,
            $"MSBuild evaluation for {key.ProjectPath} ({key.Configuration})"
        );
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(
                $"MSBuild evaluation returned empty output for {key.ProjectPath} ({key.Configuration})."
            );
        }

        return ParseEvaluatedProject(result.StandardOutput, key);
    }

    private static Task<EvaluatedRestoreGraph> EvaluateRestoreGraphAsync(
        string entryProjectPath,
        string configuration,
        IReadOnlyCollection<string>? expectedProjects
    )
    {
        var key = new EvaluationKey(Path.GetFullPath(entryProjectPath), configuration);
        return RestoreGraphCache
            .GetOrAdd(
                key,
                _ => new Lazy<Task<EvaluatedRestoreGraph>>(
                    () => RunRestoreGraphEvaluationAsync(key, expectedProjects),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;
    }

    private static async Task<EvaluatedRestoreGraph> RunRestoreGraphEvaluationAsync(
        EvaluationKey key,
        IReadOnlyCollection<string>? expectedProjects
    )
    {
        if (!File.Exists(key.ProjectPath))
            throw new InvalidOperationException(
                $"Restore graph entry project does not exist: {key.ProjectPath}"
            );

        var graphPath = Path.Combine(
            Path.GetTempPath(),
            $"travel-restore-graph-{Guid.NewGuid():N}.json"
        );
        string? aggregatorPath = null;
        try
        {
            var entryKey = key;
            if (expectedProjects is not null)
            {
                aggregatorPath = Path.Combine(
                    Path.GetTempPath(),
                    $"travel-restore-entry-{Guid.NewGuid():N}.proj"
                );
                WriteRestoreGraphAggregator(aggregatorPath, expectedProjects);
                entryKey = new EvaluationKey(aggregatorPath, key.Configuration);
            }

            var startInfo = CreateMsBuildStartInfo(entryKey);
            startInfo.ArgumentList.Add("-target:GenerateRestoreGraphFile");
            startInfo.ArgumentList.Add($"-property:RestoreGraphOutputPath={graphPath}");
            await RunProcessAsync(
                startInfo,
                $"MSBuild restore-graph evaluation for {key.ProjectPath} ({key.Configuration})"
            );

            if (!File.Exists(graphPath))
            {
                throw new InvalidOperationException(
                    $"MSBuild restore-graph evaluation did not create {graphPath}."
                );
            }

            await using var stream = new FileStream(
                graphPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true
            );
            using var reader = new StreamReader(stream);
            var graphJson = await ReadBoundedOutputAsync(
                reader,
                "restore graph",
                MaxRestoreGraphCharacters
            );
            if (string.IsNullOrWhiteSpace(graphJson))
            {
                throw new InvalidOperationException(
                    $"MSBuild restore-graph evaluation returned an empty graph for {key.ProjectPath} ({key.Configuration})."
                );
            }

            return ParseRestoreGraph(graphJson, key, expectedProjects);
        }
        finally
        {
            DeleteTemporaryFiles(graphPath, aggregatorPath);
        }
    }

    private static void WriteRestoreGraphAggregator(
        string aggregatorPath,
        IEnumerable<string> projectPaths
    )
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = true,
        };
        using var writer = XmlWriter.Create(aggregatorPath, settings);
        writer.WriteStartElement("Project");
        writer.WriteAttributeString("Sdk", "Microsoft.NET.Sdk");
        writer.WriteStartElement("PropertyGroup");
        writer.WriteElementString("TargetFramework", "net10.0");
        writer.WriteElementString("RestoreProjectStyle", "PackageReference");
        writer.WriteEndElement();
        writer.WriteStartElement("ItemGroup");
        foreach (var projectPath in projectPaths.Select(Path.GetFullPath).Order(PathComparer))
        {
            writer.WriteStartElement("ProjectReference");
            writer.WriteAttributeString("Include", projectPath);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void DeleteTemporaryFiles(params string?[] paths)
    {
        var failures = new List<Exception>();
        foreach (var path in paths.Where(path => path is not null))
        {
            try
            {
                File.Delete(path!);
            }
            catch (Exception exception)
            {
                failures.Add(
                    new IOException($"Could not delete temporary MSBuild file {path}.", exception)
                );
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "One or more temporary MSBuild files could not be deleted.",
                new AggregateException(failures)
            );
        }
    }

    private static ProcessStartInfo CreateMsBuildStartInfo(EvaluationKey key)
    {
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(dotnetHostPath) ? "dotnet" : dotnetHostPath,
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(key.ProjectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");
        startInfo.ArgumentList.Add($"-property:Configuration={key.Configuration}");
        return startInfo;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string context
    )
    {
        await MsBuildProcessSlots.WaitAsync();
        try
        {
            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("the process API returned false");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Could not launch {context}.", exception);
            }

            var standardOutput = ReadBoundedOutputAsync(
                process.StandardOutput,
                "standard output",
                MaxMsBuildOutputCharacters
            );
            var standardError = ReadBoundedOutputAsync(
                process.StandardError,
                "standard error",
                MaxMsBuildOutputCharacters
            );
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(), standardOutput, standardError)
                    .WaitAsync(MsBuildTimeout);
            }
            catch (TimeoutException exception)
            {
                TryKill(process);
                await DrainAfterKillAsync(standardOutput, standardError);
                throw new InvalidOperationException(
                    $"{context} timed out after {MsBuildTimeout.TotalSeconds:0} seconds.",
                    exception
                );
            }
            catch (Exception exception)
            {
                TryKill(process);
                await DrainAfterKillAsync(standardOutput, standardError);
                throw new InvalidOperationException($"{context} output failed.", exception);
            }

            var output = await standardOutput;
            var error = await standardError;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{context} exited with code {process.ExitCode}. stderr: {Abbreviate(error)}"
                );
            }

            return new ProcessResult(output, error);
        }
        finally
        {
            MsBuildProcessSlots.Release();
        }
    }

    private static async Task<string> ReadBoundedOutputAsync(
        StreamReader reader,
        string streamName,
        int maxCharacters
    )
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
                return output.ToString();

            if (output.Length + read > maxCharacters)
            {
                throw new InvalidOperationException(
                    $"MSBuild {streamName} exceeded {maxCharacters} characters."
                );
            }

            output.Append(buffer, 0, read);
        }
    }

    private static EvaluatedProject ParseEvaluatedProject(string output, EvaluationKey key)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Object
            )
            {
                throw new InvalidOperationException("JSON does not contain an Items object.");
            }

            var items = new Dictionary<string, EvaluatedItem[]>(StringComparer.Ordinal);
            foreach (var itemName in DependencyItemNames)
            {
                if (
                    !itemsElement.TryGetProperty(itemName, out var groupElement)
                    || groupElement.ValueKind != JsonValueKind.Array
                )
                {
                    throw new InvalidOperationException(
                        $"JSON does not contain the required {itemName} array."
                    );
                }

                items[itemName] = groupElement
                    .EnumerateArray()
                    .Select(item => ParseEvaluatedItem(itemName, item))
                    .ToArray();
            }

            return new EvaluatedProject(items);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild evaluation returned invalid JSON for {key.ProjectPath} ({key.Configuration}).",
                exception
            );
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild evaluation returned incomplete JSON for {key.ProjectPath} ({key.Configuration}): {exception.Message}",
                exception
            );
        }
    }

    private static EvaluatedRestoreGraph ParseRestoreGraph(
        string output,
        EvaluationKey key,
        IReadOnlyCollection<string>? expectedProjects
    )
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.Number
                || format.GetInt32() != 1
                || !root.TryGetProperty("projects", out var projectsElement)
                || projectsElement.ValueKind != JsonValueKind.Object
            )
            {
                throw new InvalidOperationException(
                    "JSON does not contain a format-1 projects object."
                );
            }

            var projectReferences = new Dictionary<string, string[]>(PathComparer);
            var frameworkCounts = new Dictionary<string, int>(PathComparer);
            foreach (var projectProperty in projectsElement.EnumerateObject())
            {
                var projectPath = Path.GetFullPath(projectProperty.Name);
                var project = projectProperty.Value;
                if (
                    project.ValueKind != JsonValueKind.Object
                    || !project.TryGetProperty("restore", out var restore)
                    || restore.ValueKind != JsonValueKind.Object
                    || !restore.TryGetProperty("projectPath", out var projectPathElement)
                    || projectPathElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(projectPathElement.GetString())
                    || !PathsEqual(projectPath, projectPathElement.GetString()!)
                    || !restore.TryGetProperty("frameworks", out var frameworks)
                    || frameworks.ValueKind != JsonValueKind.Object
                )
                {
                    throw new InvalidOperationException(
                        $"Restore graph project {projectPath} is missing required projectPath/frameworks metadata."
                    );
                }

                var references = new HashSet<string>(PathComparer);
                var frameworkCount = 0;
                foreach (var frameworkProperty in frameworks.EnumerateObject())
                {
                    if (
                        string.IsNullOrWhiteSpace(frameworkProperty.Name)
                        || frameworkProperty.Value.ValueKind != JsonValueKind.Object
                    )
                    {
                        throw new InvalidOperationException(
                            $"Restore graph framework {frameworkProperty.Name} for {projectPath} is not an object."
                        );
                    }

                    frameworkCount += 1;

                    if (
                        !frameworkProperty.Value.TryGetProperty(
                            "projectReferences",
                            out var referencesElement
                        )
                    )
                    {
                        continue;
                    }

                    if (referencesElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidOperationException(
                            $"Restore graph projectReferences for {projectPath}/{frameworkProperty.Name} is not an object."
                        );
                    }

                    foreach (var referenceProperty in referencesElement.EnumerateObject())
                    {
                        if (
                            referenceProperty.Value.ValueKind != JsonValueKind.Object
                            || !referenceProperty.Value.TryGetProperty(
                                "projectPath",
                                out var referencePathElement
                            )
                            || referencePathElement.ValueKind != JsonValueKind.String
                            || string.IsNullOrWhiteSpace(referencePathElement.GetString())
                        )
                        {
                            throw new InvalidOperationException(
                                $"Restore graph reference {referenceProperty.Name} for {projectPath} is missing projectPath metadata."
                            );
                        }

                        var referencePath = Path.GetFullPath(referencePathElement.GetString()!);
                        if (!PathsEqual(referenceProperty.Name, referencePath))
                        {
                            throw new InvalidOperationException(
                                $"Restore graph reference key/path mismatch for {projectPath}: {referenceProperty.Name} vs {referencePath}."
                            );
                        }

                        references.Add(referencePath);
                    }
                }

                projectReferences.Add(projectPath, [.. references]);
                frameworkCounts.Add(projectPath, frameworkCount);
            }

            if (expectedProjects is not null)
            {
                var expected = expectedProjects.Select(Path.GetFullPath).ToHashSet(PathComparer);
                var repositoryGraphProjects = projectReferences
                    .Keys.Where(IsWithinRepository)
                    .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(PathComparer);
                if (!expected.SetEquals(repositoryGraphProjects))
                {
                    var missing = expected.Except(repositoryGraphProjects, PathComparer);
                    var unexpected = repositoryGraphProjects.Except(expected, PathComparer);
                    throw new InvalidOperationException(
                        $"Restore graph project inventory mismatch. Missing: {FormatPaths(missing)}. Unexpected: {FormatPaths(unexpected)}."
                    );
                }

                foreach (var projectPath in expected)
                {
                    if (!frameworkCounts.TryGetValue(projectPath, out var count) || count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Restore graph project {projectPath} must contain at least one usable framework object."
                        );
                    }
                }
            }

            return new EvaluatedRestoreGraph(projectReferences);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild restore graph returned invalid JSON for {key.ProjectPath} ({key.Configuration}).",
                exception
            );
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild restore graph returned incomplete JSON for {key.ProjectPath} ({key.Configuration}): {exception.Message}",
                exception
            );
        }
    }

    private static string FormatPaths(IEnumerable<string> paths)
    {
        var values = paths.ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static EvaluatedItem ParseEvaluatedItem(string itemName, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{itemName} contains a non-object item.");

        var metadata = item.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property =>
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText(),
                StringComparer.OrdinalIgnoreCase
            );
        var identity = RequireMetadata(itemName, metadata, "Identity");
        var fullPath = RequireMetadata(itemName, metadata, "FullPath");
        var definingProjectFullPath = RequireMetadata(
            itemName,
            metadata,
            "DefiningProjectFullPath"
        );
        if (!Path.IsPathFullyQualified(fullPath))
            throw new InvalidOperationException($"{itemName} FullPath is not absolute: {fullPath}");
        if (!Path.IsPathFullyQualified(definingProjectFullPath))
        {
            throw new InvalidOperationException(
                $"{itemName} DefiningProjectFullPath is not absolute: {definingProjectFullPath}"
            );
        }

        return new EvaluatedItem(identity, fullPath, definingProjectFullPath, metadata);
    }

    private static string RequireMetadata(
        string itemName,
        IReadOnlyDictionary<string, string> metadata,
        string metadataName
    )
    {
        if (!metadata.TryGetValue(metadataName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{itemName} item is missing required {metadataName} metadata."
            );
        }

        return value;
    }

    private static async Task DrainAfterKillAsync(params Task<string>[] readers)
    {
        try
        {
            await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Preserve the launch, timeout, or output-bound failure that triggered cleanup.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cleanup is best-effort; the evaluation still fails closed.
        }
    }

    private static string Abbreviate(string value) =>
        value.Length <= 2000 ? value : $"{value[..2000]}...<truncated>";

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static bool IsWithinRepository(string path)
    {
        var relativePath = Path.GetRelativePath(RepositoryRoot, Path.GetFullPath(path));
        return !Path.IsPathFullyQualified(relativePath)
            && !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal
            );
    }

    private static bool DeclaresClrType(string source, string messageName) =>
        Regex.IsMatch(
            StripComments(source),
            $@"(?m)^\s*(?:\[[^\]\r\n]*\]\s*)*(?:(?:file|public|protected|internal|private|new|abstract|sealed|static|partial|readonly|ref|unsafe)\s+)*(?:(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+@?{Regex.Escape(messageName)}\b|delegate\s+[^;{{}}]*?\b@?{Regex.Escape(messageName)}\b(?=\s*(?:<[^;{{}}]*?>)?\s*\())"
        );

    private static IEnumerable<string> EnumerateFiles(string searchPattern, params string[] roots)
    {
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, searchPattern))
                yield return file;

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (IgnoredDirectoryNames.Contains(Path.GetFileName(child)))
                    continue;

                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    continue;

                pending.Push(child);
            }
        }
    }

    private static string StripComments(string source) =>
        Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "Travel.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root containing Travel.slnx."
        );
    }

    private sealed record EvaluatedProject(IReadOnlyDictionary<string, EvaluatedItem[]> Items);

    private sealed record EvaluatedRestoreGraph(
        IReadOnlyDictionary<string, string[]> ProjectReferences
    );

    private sealed record EvaluatedItem(
        string Identity,
        string FullPath,
        string DefiningProjectFullPath,
        IReadOnlyDictionary<string, string> Metadata
    );

    private readonly record struct EvaluationKey(string ProjectPath, string Configuration);

    private readonly record struct ProcessResult(string StandardOutput, string StandardError);
}
