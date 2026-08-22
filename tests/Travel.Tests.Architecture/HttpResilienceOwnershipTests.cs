using System.Text;
using Shouldly;
using Xunit;

namespace Travel.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class HttpResilienceOwnershipTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Production_sources_do_not_suppress_globally_registered_resilience()
    {
        var violations = ProductionSources()
            .Select(path => new
            {
                Path = path,
                Code = StripCommentsAndLiterals(File.ReadAllText(path)),
            })
            .Where(source =>
                source.Code.Contains("RemoveAllResilienceHandlers", StringComparison.Ordinal)
                || source.Code.Contains("ReplaceGlobalResilience", StringComparison.Ordinal)
            )
            .Select(source => Path.GetRelativePath(RepositoryRoot, source.Path))
            .ToArray();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Standard_resilience_handler_is_owned_only_by_platform_opt_in()
    {
        var owner = Path.Combine(RepositoryRoot, "apps", "Travel.ServiceDefaults", "Extensions.cs");
        var ownerCode = StripCommentsAndLiterals(File.ReadAllText(owner));
        var methodStart = FindInvocations(ownerCode, "AddPlatformHttpResilience").Single();
        methodStart.ShouldBeGreaterThanOrEqualTo(0);
        var bodyStart = ownerCode.IndexOf('{', methodStart);
        bodyStart.ShouldBeGreaterThan(methodStart);
        var bodyEnd = FindMatchingBrace(ownerCode, bodyStart);

        var invocations = ProductionSources()
            .SelectMany(path =>
                FindInvocations(
                        StripCommentsAndLiterals(File.ReadAllText(path)),
                        "AddStandardResilienceHandler"
                    )
                    .Select(index => new { Path = path, Index = index })
            )
            .ToArray();

        invocations.Length.ShouldBe(1);
        Path.GetFullPath(invocations[0].Path)
            .ShouldBe(Path.GetFullPath(owner), StringCompareShould.IgnoreCase);
        invocations[0].Index.ShouldBeGreaterThan(bodyStart);
        invocations[0].Index.ShouldBeLessThan(bodyEnd);
    }

    [Fact]
    public void Invocation_scanner_detects_whitespace_mutation_and_ignores_trivia()
    {
        const string source = """
            client.AddStandardResilienceHandler
                (
                );
            // client.AddStandardResilienceHandler ();
            var text = "client.AddStandardResilienceHandler ();";
            """;

        FindInvocations(StripCommentsAndLiterals(source), "AddStandardResilienceHandler")
            .Count()
            .ShouldBe(1);
    }

    private static IEnumerable<string> ProductionSources()
    {
        foreach (var rootName in new[] { "apps", "modules", "shared" })
        {
            var root = Path.Combine(RepositoryRoot, rootName);
            foreach (
                var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            )
            {
                var relative = Path.GetRelativePath(root, path);
                var segments = relative.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
                if (
                    segments.Any(segment =>
                        segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                        || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                        || segment.Equals("Generated", StringComparison.OrdinalIgnoreCase)
                    )
                )
                    continue;
                if (
                    path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                yield return path;
            }
        }
    }

    private static IEnumerable<int> FindInvocations(string source, string identifier)
    {
        for (var index = 0; ; )
        {
            index = source.IndexOf(identifier, index, StringComparison.Ordinal);
            if (index < 0)
                yield break;

            var identifierEnd = index + identifier.Length;
            var hasIdentifierBoundary =
                (index == 0 || !IsIdentifierCharacter(source[index - 1]))
                && (
                    identifierEnd == source.Length || !IsIdentifierCharacter(source[identifierEnd])
                );
            var cursor = identifierEnd;
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
                cursor++;

            if (hasIdentifierBoundary && cursor < source.Length && source[cursor] == '(')
                yield return index;

            index = identifierEnd;
        }
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static int FindMatchingBrace(string source, int openingBrace)
    {
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return index;
        }

        throw new InvalidOperationException("Could not find the end of AddPlatformHttpResilience.");
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

            if (source[index] is '"' or '\'')
            {
                var delimiter = source[index++];
                output.Append(' ');
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
                continue;
            }

            output.Append(source[index++]);
        }

        return output.ToString();
    }

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

        throw new DirectoryNotFoundException("Could not locate Travel.slnx.");
    }
}
