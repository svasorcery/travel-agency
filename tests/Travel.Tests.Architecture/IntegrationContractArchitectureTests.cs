using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
    public void Contract_project_declares_only_the_leaf_dependency_allowlist()
    {
        var projectPath = Path.Combine(RepositoryRoot, ContractProjectRelativePath);

        FindProjectDependencyViolations(XDocument.Load(projectPath)).ShouldBeEmpty();
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
    [InlineData("PackageReference", "Newtonsoft.Json", false)]
    [InlineData("ProjectReference", "../Travel.Shared.Domain/Travel.Shared.Domain.csproj", false)]
    [InlineData("FrameworkReference", "Microsoft.AspNetCore.App", false)]
    [InlineData("PackageReference", "Newtonsoft.Json", true)]
    [InlineData("ProjectReference", "../Travel.Shared.Domain/Travel.Shared.Domain.csproj", true)]
    public void Leaf_dependency_validator_rejects_unexpected_project_dependencies(
        string itemName,
        string include,
        bool usesDefaultXmlNamespace
    )
    {
        var namespaceDeclaration = usesDefaultXmlNamespace
            ? " xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\""
            : string.Empty;
        var project = XDocument.Parse(
            $"""
            <Project Sdk="Microsoft.NET.Sdk"{namespaceDeclaration}>
              <ItemGroup>
                <PackageReference Include="WolverineFx" />
                <{itemName} Include="{include}" />
              </ItemGroup>
            </Project>
            """
        );

        FindProjectDependencyViolations(project).ShouldNotBeEmpty();
    }

    [Fact]
    public void Leaf_dependency_validator_requires_exactly_one_direct_WolverineFx_package()
    {
        var missingPackage = XDocument.Parse("<Project />");
        var duplicatePackage = XDocument.Parse(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="WolverineFx" />
                <PackageReference Include="WolverineFx" />
              </ItemGroup>
            </Project>
            """
        );

        FindProjectDependencyViolations(missingPackage)
            .ShouldContain(
                "package references: expected exactly one direct WolverineFx; found none"
            );
        FindProjectDependencyViolations(duplicatePackage)
            .ShouldContain(
                "package references: expected exactly one direct WolverineFx; found WolverineFx, WolverineFx"
            );
    }

    [Fact]
    public void Only_approved_projects_directly_reference_the_contract_and_required_consumers_do()
    {
        var contractProjectPath = Path.GetFullPath(
            Path.Combine(RepositoryRoot, ContractProjectRelativePath)
        );
        File.Exists(contractProjectPath)
            .ShouldBeTrue("the contract project is required for MSBuild graph inspection");

        var consumers = FindDirectProjectConsumers(contractProjectPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var approved = new[]
        {
            "modules/flights/Travel.Modules.Flights.Application/Travel.Modules.Flights.Application.csproj",
            "apps/Travel.AI/Travel.AI.csproj",
            "modules/flights/Travel.Modules.Flights.Api/Travel.Modules.Flights.Api.csproj",
            "tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj",
        }
            .Select(path => Path.GetFullPath(Path.Combine(RepositoryRoot, path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        consumers.ShouldBeSubsetOf(approved);
        consumers.ShouldContain(
            Path.GetFullPath(
                Path.Combine(
                    RepositoryRoot,
                    "modules/flights/Travel.Modules.Flights.Application/Travel.Modules.Flights.Application.csproj"
                )
            )
        );
        consumers.ShouldContain(
            Path.GetFullPath(Path.Combine(RepositoryRoot, "apps/Travel.AI/Travel.AI.csproj"))
        );
        consumers.ShouldContain(
            Path.GetFullPath(
                Path.Combine(
                    RepositoryRoot,
                    "tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj"
                )
            )
        );
    }

    [Theory]
    [InlineData(
        @"..\..\shared\dotnet\Travel.IntegrationContracts.AI\Travel.IntegrationContracts.AI.csproj",
        '/',
        "../../shared/dotnet/Travel.IntegrationContracts.AI/Travel.IntegrationContracts.AI.csproj"
    )]
    [InlineData(
        "../../shared/dotnet/Travel.IntegrationContracts.AI/Travel.IntegrationContracts.AI.csproj",
        '\\',
        @"..\..\shared\dotnet\Travel.IntegrationContracts.AI\Travel.IntegrationContracts.AI.csproj"
    )]
    public void Project_reference_include_normalization_accepts_both_msbuild_slash_forms(
        string include,
        char simulatedDirectorySeparator,
        string expected
    )
    {
        NormalizeProjectReferenceInclude(include, simulatedDirectorySeparator).ShouldBe(expected);
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

    private static IEnumerable<string> FindDirectProjectConsumers(string contractProjectPath)
    {
        return EnumerateFiles("*.csproj", RepositoryRoot)
            .Where(path =>
                !Path.GetFullPath(path)
                    .Equals(contractProjectPath, StringComparison.OrdinalIgnoreCase)
            )
            .Where(path =>
                XDocument
                    .Load(path)
                    .Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Any(reference =>
                    {
                        var include = reference.Attribute("Include")?.Value;
                        return include is not null
                            && Path.GetFullPath(
                                    Path.Combine(
                                        Path.GetDirectoryName(path)!,
                                        NormalizeProjectReferenceInclude(
                                            include,
                                            Path.DirectorySeparatorChar
                                        )
                                    )
                                )
                                .Equals(contractProjectPath, StringComparison.OrdinalIgnoreCase);
                    })
            );
    }

    private static string NormalizeProjectReferenceInclude(
        string include,
        char directorySeparator
    ) => include.Replace('\\', directorySeparator).Replace('/', directorySeparator);

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

    private static string[] FindProjectDependencyViolations(XDocument project)
    {
        var dependencies = project
            .Descendants()
            .Where(element =>
                element.Name.LocalName
                    is "PackageReference"
                        or "ProjectReference"
                        or "FrameworkReference"
            )
            .ToArray();
        var packages = dependencies
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? "<missing Include>")
            .ToArray();
        var violations = dependencies
            .Where(element => element.Name.LocalName is "ProjectReference" or "FrameworkReference")
            .Select(element =>
                $"{element.Name.LocalName}: {element.Attribute("Include")?.Value ?? "<missing Include>"}"
            )
            .ToList();

        if (packages.Length != 1 || !packages[0].Equals("WolverineFx", StringComparison.Ordinal))
        {
            violations.Add(
                $"package references: expected exactly one direct WolverineFx; found {(packages.Length == 0 ? "none" : string.Join(", ", packages))}"
            );
        }

        return [.. violations];
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
}
