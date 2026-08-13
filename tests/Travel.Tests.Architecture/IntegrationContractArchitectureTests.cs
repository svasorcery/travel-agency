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
    public void Contract_assembly_is_a_leaf_without_web_persistence_or_travel_dependencies()
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{ContractAssemblyName}.dll");
        File.Exists(assemblyPath)
            .ShouldBeTrue("the contract DLL is required for dependency inspection");

        var references = Assembly
            .LoadFrom(assemblyPath)
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty);
        references.ShouldNotContain(name => name.StartsWith("Travel.", StringComparison.Ordinal));
        references.ShouldNotContain(name =>
            name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
        );
        references.ShouldNotContain(name =>
            name.Contains("EntityFramework", StringComparison.Ordinal)
        );
        references.ShouldNotContain(name => name.Contains("Marten", StringComparison.Ordinal));
        references.ShouldNotContain(name => name.Contains("ErrorOr", StringComparison.Ordinal));
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
    public void Production_declaration_detector_recognizes_all_supported_clr_type_forms(
        string source,
        string messageName
    )
    {
        DeclaresClrType(source, messageName).ShouldBeTrue();
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
                    .Descendants("ProjectReference")
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

    private static bool DeclaresClrType(string source, string messageName) =>
        Regex.IsMatch(
            StripComments(source),
            $@"(?m)^\s*(?:(?:file|public|protected|internal|private|new|abstract|sealed|static|partial|readonly|ref|unsafe)\s+)*(?:class|struct|record(?:\s+(?:class|struct))?)\s+{Regex.Escape(messageName)}\b"
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
