using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal static class HotelsSourceManifest
{
    private const string M2Evidence = "TestResults/hotels-m2-memory-load-acceptance/";
    private const string M2Archive =
        "openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance/";
    private const string M2Change = "openspec/changes/hotels-m2-memory-load-acceptance/";

    public static void VerifyFile(string root, string path, IReadOnlyList<string>? profiles = null)
    {
        if (profiles is not null)
        {
            var relative = Path.GetRelativePath(
                    root,
                    Path.GetFullPath(path, Path.GetFullPath(root))
                )
                .Replace('\\', '/');
            if (!relative.StartsWith(M2Evidence, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "M2 manifest must belong to its evidence tree."
                );
            path = PhysicalFile(root, relative);
            if (!Within(Path.Combine(root, M2Evidence), path))
                throw new InvalidOperationException(
                    "Physical M2 manifest leaves its evidence tree."
                );
        }
        var manifest =
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Manifest is missing.");
        Verify(root, manifest, profiles);
    }

    public static void Verify(
        string root,
        Dictionary<string, string> manifest,
        IReadOnlyList<string>? profiles = null
    )
    {
        var expectedProfiles = profiles is null
            ? new[]
            {
                "TestResults/hotels-m1/l1-profile.md",
                "TestResults/hotels-m1/l1-memory-diagnostic-profile.md",
            }
            : profiles.Select(p => M2Profile(root, p)).ToArray();
        if (
            expectedProfiles.Length == 0
            || expectedProfiles.Distinct(StringComparer.Ordinal).Count() != expectedProfiles.Length
        )
            throw new InvalidOperationException("Explicit unique profiles are required.");
        var git = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "ls-files", "-z", "-c", "-o", "--exclude-standard" })
            git.ArgumentList.Add(arg);
        using var child =
            Process.Start(git)
            ?? throw new InvalidOperationException("Manifest inventory unavailable.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(10000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new InvalidOperationException("Manifest inventory exceeded its budget.");
        }
        var paths = stdout
            .GetAwaiter()
            .GetResult()
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p =>
                !new[] { "TestResults/", "docs/", "openspec/", ".agents/", ".codex/" }.Any(prefix =>
                    p.StartsWith(prefix, StringComparison.Ordinal)
                )
            )
            .ToHashSet(StringComparer.Ordinal);
        _ = stderr.GetAwaiter().GetResult(); // Do not echo local paths/configuration from a failed subprocess.
        paths.UnionWith(expectedProfiles);
        if (child.ExitCode != 0 || !paths.SetEquals(manifest.Keys))
            throw new InvalidOperationException(
                "Source manifest is incomplete or file inventory changed."
            );
        foreach (var (relative, hash) in manifest)
            if (
                !string.Equals(
                    Convert.ToHexString(
                        SHA256.HashData(File.ReadAllBytes(PhysicalFile(root, relative)))
                    ),
                    hash,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException("Source manifest changed.");
    }

    private static string M2Profile(string root, string value)
    {
        if (Path.IsPathRooted(value))
            throw new InvalidOperationException("Profile must be repository-relative.");
        var relative = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(root, value)))
            .Replace('\\', '/');
        if (
            !(
                relative.StartsWith(M2Change, StringComparison.Ordinal)
                || relative.StartsWith(M2Archive, StringComparison.Ordinal)
                || relative.StartsWith(M2Evidence, StringComparison.Ordinal)
            )
            || !(
                relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new InvalidOperationException("Profile leaves the M2 planning/evidence tree.");
        var physical = PhysicalFile(root, relative);
        var boundary = Path.Combine(
            root,
            relative.StartsWith(M2Change, StringComparison.Ordinal) ? M2Change
                : relative.StartsWith(M2Archive, StringComparison.Ordinal) ? M2Archive
                : M2Evidence
        );
        if (!Within(boundary, physical))
            throw new InvalidOperationException("Physical profile leaves its selected tree.");
        return relative;
    }

    private static string PhysicalFile(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(root, full))
            throw new InvalidOperationException("Source manifest leaves its repository.");
        var current = root;
        foreach (
            var segment in Path.GetRelativePath(root, full)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        )
        {
            current = Path.Combine(current, segment);
            FileSystemInfo item = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!item.Exists)
                throw new InvalidOperationException("Manifest source is missing.");
            var target = item.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
                current = Path.GetFullPath(target.FullName);
            if (!Within(root, current))
                throw new InvalidOperationException("Physical source leaves its repository.");
        }
        return current;
    }

    private static bool Within(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative.Length > 0
            && relative != "."
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
