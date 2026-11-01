using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2DirectoryInventory(string Path, Dictionary<string, string> Files);

internal sealed record HotelM2BuildInventory(
    string BuildRoot,
    Dictionary<string, string> BuildFiles,
    string DotnetPath,
    string DotnetHash,
    HotelM2DirectoryInventory[] Frameworks
)
{
    internal static void Verify(string root, string path)
    {
        var value =
            JsonSerializer.Deserialize<HotelM2BuildInventory>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            ) ?? throw new InvalidDataException("Build inventory absent.");
        var expectedBuild = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(root, "tools/demo/Travel.Hotels.Fixture/bin/Debug/net10.0")
        );
        if (
            System.IO.Path.GetFullPath(System.IO.Path.Combine(root, value.BuildRoot))
            != expectedBuild
        )
            throw new InvalidDataException("Wrong build root.");
        VerifyDirectory(expectedBuild, value.BuildFiles);
        if (
            !string.Equals(
                Environment.ProcessPath,
                value.DotnetPath,
                StringComparison.OrdinalIgnoreCase
            )
            || Hash(value.DotnetPath) != value.DotnetHash
        )
            throw new InvalidDataException("Frozen dotnet host changed.");
        var expectedFrameworks = new[]
        {
            RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetDirectoryName(typeof(WebApplication).Assembly.Location)!,
        }.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (
            value.Frameworks.Length != 2
            || !expectedFrameworks.SetEquals(
                value.Frameworks.Select(x => x.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar))
            )
        )
            throw new InvalidDataException("Frozen runtime selection changed.");
        foreach (var framework in value.Frameworks)
            VerifyDirectory(framework.Path, framework.Files);
    }

    internal static void VerifyDirectory(
        string directory,
        IReadOnlyDictionary<string, string> files
    )
    {
        directory = System.IO.Path.GetFullPath(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Redirected inventory root.");
        var actual = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string current)
        {
            foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Redirected inventory entry.");
                if (entry is DirectoryInfo child)
                    Visit(child.FullName);
                else
                    actual.Add(
                        System.IO.Path.GetRelativePath(directory, entry.FullName).Replace('\\', '/')
                    );
            }
        }
        Visit(directory);
        if (files.Count == 0 || !actual.SetEquals(files.Keys))
            throw new InvalidDataException("Frozen file inventory changed.");
        foreach (var pair in files)
        {
            if (
                System.IO.Path.IsPathRooted(pair.Key)
                || pair.Key.Split('/').Any(p => p is "." or "..")
            )
                throw new InvalidDataException("Inventory path escaped.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, pair.Key));
            if (
                !path.StartsWith(
                    directory + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
                || Hash(path) != pair.Value
            )
                throw new InvalidDataException("Frozen file bytes changed.");
        }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
