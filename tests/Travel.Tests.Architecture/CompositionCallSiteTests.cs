using System.Text;
using Shouldly;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class CompositionCallSiteTests
{
    private static readonly HashSet<string> ExcludedSourceDirectoryNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ".scratch",
        ".tmp",
        "bin",
        "Generated",
        "obj",
        "scratch",
        "TestResults",
        "tmp",
    };
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("AddMarten", 1)]
    [InlineData("UseWolverine", 1)]
    [InlineData("MapWolverineEndpoints", 1)]
    public void Host_owns_each_global_Critter_Stack_call_exactly_once(
        string invocation,
        int expectedCount
    )
    {
        var hostDirectory = Path.Combine(RepositoryRoot, "apps", "Travel.Host");

        CountInvocationsInProductionSources(hostDirectory, invocation).ShouldBe(expectedCount);
    }

    [Theory]
    [InlineData("AddMarten")]
    [InlineData("UseWolverine")]
    [InlineData("MapWolverineEndpoints")]
    public void Production_module_sources_do_not_own_global_Critter_Stack_calls(string invocation)
    {
        var modulesRoot = Path.Combine(RepositoryRoot, "modules");

        FindModuleGlobalCallSiteViolations(modulesRoot, invocation).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Travel.Modules.Flights.Core", "AddMarten")]
    [InlineData("Travel.Modules.Flights.Application", "UseWolverine")]
    [InlineData("Travel.Modules.Flights.Infrastructure", "MapWolverineEndpoints")]
    public void Module_global_call_guard_rejects_controlled_layer_bypass(
        string projectRoot,
        string invocation
    )
    {
        using var fixture = new ProductionSourceFixture();
        fixture.Write($"{projectRoot}/Bypass.cs", $"{invocation}();");

        FindModuleGlobalCallSiteViolations(fixture.Root, invocation)
            .ShouldHaveSingleItem()
            .ShouldEndWith("Bypass.cs");
    }

    [Theory]
    [InlineData("UseSnakeCaseNamingConvention", 1)]
    [InlineData("MigrationsHistoryTable", 1)]
    public void Flights_EF_provider_conventions_have_one_internal_owner(
        string invocation,
        int expectedCount
    )
    {
        var flightsRoot = Path.Combine(RepositoryRoot, "modules", "flights");
        var apiDirectory = Path.Combine(flightsRoot, "Travel.Modules.Flights.Api");
        var infrastructureDirectory = Path.Combine(
            flightsRoot,
            "Travel.Modules.Flights.Infrastructure"
        );
        var owner = Path.Combine(
            infrastructureDirectory,
            "Persistence",
            "Flights" + "DbContextConfiguration.cs"
        );
        var productionSources = ProductionSourceFiles(apiDirectory)
            .Concat(ProductionSourceFiles(infrastructureDirectory));

        CountInvocations(File.ReadAllText(owner), invocation).ShouldBe(expectedCount);
        productionSources
            .Where(path =>
                !Path.GetFullPath(path)
                    .Equals(Path.GetFullPath(owner), StringComparison.OrdinalIgnoreCase)
            )
            .Sum(path => CountInvocations(File.ReadAllText(path), invocation))
            .ShouldBe(0);
    }

    [Fact]
    public void Invocation_counter_ignores_comments_strings_chars_and_raw_strings()
    {
        const string source = """"
            RealCall();
            // RealCall();
            /* RealCall(); */
            var regular = "RealCall();";
            var verbatim = @"RealCall();";
            var raw = """RealCall();""";
            var character = '(';
            Real/* separator */Call();
            """";

        CountInvocations(source, "RealCall").ShouldBe(1);
    }

    [Fact]
    public void Production_source_scanner_recurses_and_excludes_generated_or_scratch_trees()
    {
        using var fixture = new ProductionSourceFixture();
        fixture.Write("Program.cs");
        fixture.Write("Features/OtherHostSource.cs");
        fixture.Write("bin/Ignored.cs");
        fixture.Write("obj/Ignored.cs");
        fixture.Write("Generated/Ignored.cs");
        fixture.Write("scratch/Ignored.cs");
        fixture.Write("Features/Ignored.g.cs");
        fixture.Write("Features/Ignored.generated.cs");

        CountInvocationsInProductionSources(fixture.Root, "OwnedCall").ShouldBe(2);
    }

    [Fact]
    public void Production_source_scanner_combines_all_owned_roots()
    {
        using var fixture = new ProductionSourceFixture();
        var apiRoot = fixture.Write("Api/Composition.cs");
        var infrastructureRoot = fixture.Write("Infrastructure/Persistence.cs");

        CountInvocationsInProductionSources(
                [Path.GetDirectoryName(apiRoot)!, Path.GetDirectoryName(infrastructureRoot)!],
                "OwnedCall"
            )
            .ShouldBe(2);
    }

    private static string[] FindModuleGlobalCallSiteViolations(
        string modulesRoot,
        string invocation
    ) =>
        ProductionSourceFiles(modulesRoot)
            .Where(path => CountInvocations(File.ReadAllText(path), invocation) > 0)
            .Select(path => Path.GetRelativePath(modulesRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static int CountInvocationsInProductionSources(string root, string identifier) =>
        ProductionSourceFiles(root)
            .Sum(path => CountInvocations(File.ReadAllText(path), identifier));

    private static int CountInvocationsInProductionSources(
        IEnumerable<string> roots,
        string identifier
    ) => roots.Sum(root => CountInvocationsInProductionSources(root, identifier));

    private static IEnumerable<string> ProductionSourceFiles(string root)
    {
        var rootDirectory = new DirectoryInfo(root);
        if (!rootDirectory.Exists)
            throw new DirectoryNotFoundException(
                $"Production source root does not exist: {rootDirectory.FullName}"
            );

        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootDirectory);
        while (pending.TryPop(out var current))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            foreach (var file in current.EnumerateFiles("*.cs", SearchOption.TopDirectoryOnly))
            {
                if (!IsGeneratedSource(file.Name))
                    yield return file.FullName;
            }

            foreach (var child in current.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
            {
                if (
                    ExcludedSourceDirectoryNames.Contains(child.Name)
                    || (child.Attributes & FileAttributes.ReparsePoint) != 0
                )
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool IsGeneratedSource(string fileName) =>
        fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);

    private static int CountInvocations(string source, string identifier)
    {
        var code = StripCommentsAndLiterals(source);
        var count = 0;
        for (var index = 0; index <= code.Length - identifier.Length; index++)
        {
            if (!code.AsSpan(index).StartsWith(identifier, StringComparison.Ordinal))
                continue;
            if (index > 0 && IsIdentifierCharacter(code[index - 1]))
                continue;

            var cursor = index + identifier.Length;
            if (cursor < code.Length && IsIdentifierCharacter(code[cursor]))
                continue;
            while (cursor < code.Length && char.IsWhiteSpace(code[cursor]))
                cursor++;
            if (cursor < code.Length && code[cursor] == '(')
                count++;
        }

        return count;
    }

    private static string StripCommentsAndLiterals(string source)
    {
        var output = new StringBuilder(source.Length);
        for (var index = 0; index < source.Length; )
        {
            if (source.AsSpan(index).StartsWith("//"))
            {
                output.Append(' ');
                index += 2;
                while (index < source.Length && source[index] != '\n')
                    index++;
                continue;
            }

            if (source.AsSpan(index).StartsWith("/*"))
            {
                output.Append(' ');
                index += 2;
                while (index < source.Length && !source.AsSpan(index).StartsWith("*/"))
                    index++;
                index = Math.Min(index + 2, source.Length);
                continue;
            }

            if (source[index] == '@' && index + 1 < source.Length && source[index + 1] == '"')
            {
                output.Append(' ');
                index += 2;
                while (index < source.Length)
                {
                    if (source[index] != '"')
                    {
                        index++;
                        continue;
                    }

                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }
                continue;
            }

            if (source[index] == '"')
            {
                output.Append(' ');
                var delimiterLength = 1;
                while (
                    index + delimiterLength < source.Length
                    && source[index + delimiterLength] == '"'
                )
                {
                    delimiterLength++;
                }

                if (delimiterLength >= 3)
                {
                    index += delimiterLength;
                    while (
                        index < source.Length && !HasQuoteDelimiter(source, index, delimiterLength)
                    )
                    {
                        index++;
                    }
                    index = Math.Min(index + delimiterLength, source.Length);
                    continue;
                }

                index = SkipEscapedLiteral(source, index + 1, '"');
                continue;
            }

            if (source[index] == '\'')
            {
                output.Append(' ');
                index = SkipEscapedLiteral(source, index + 1, '\'');
                continue;
            }

            output.Append(source[index]);
            index++;
        }

        return output.ToString();
    }

    private static int SkipEscapedLiteral(string source, int index, char delimiter)
    {
        var escaped = false;
        while (index < source.Length)
        {
            var current = source[index++];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current == delimiter)
                break;
        }

        return index;
    }

    private static bool HasQuoteDelimiter(string source, int index, int length) =>
        index + length <= source.Length && source.AsSpan(index, length).IndexOfAnyExcept('"') < 0;

    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

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

file sealed class ProductionSourceFixture : IDisposable
{
    public ProductionSourceFixture()
    {
        Root = Directory.CreateTempSubdirectory("travel-composition-sources-").FullName;
    }

    public string Root { get; }

    public string Write(string relativePath, string source = "OwnedCall();")
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
