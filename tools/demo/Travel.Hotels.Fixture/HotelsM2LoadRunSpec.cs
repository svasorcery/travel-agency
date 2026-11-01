using System.Text.Json;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2LoadRunSpec(
    int Version,
    string Code,
    DateOnly CheckIn,
    string Output,
    string Manifest,
    string[] Inputs,
    string BuildInventory,
    string DatabaseImage,
    string DatabaseImageId,
    long MinimumAvailableMemoryBytes = 4294967296,
    long MinimumFreeDiskBytes = 5368709120,
    long MaximumEvidenceBytes = 536870912,
    int SetupSeconds = 120,
    int TimedSeconds = 360,
    int CleanupSeconds = 60
)
{
    internal HotelM2LoadProfile Profile => new(Code, CheckIn);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    internal static HotelM2LoadRunSpec Read(string root, string path)
    {
        var source = Resolve(root, Path.GetRelativePath(root, Path.GetFullPath(path)), false);
        var spec =
            JsonSerializer.Deserialize<HotelM2LoadRunSpec>(File.ReadAllText(source), Json)
            ?? throw new InvalidDataException("M2 profile missing.");
        if (
            !HotelsM2MemoryAssessment.SupportsVersion(spec.Version)
            || spec.Code is not ("S" or "L")
            || spec.Inputs.Length == 0
            || spec.Inputs.Distinct(StringComparer.Ordinal).Count() != spec.Inputs.Length
            || spec.SetupSeconds != 120
            || spec.TimedSeconds != 360
            || spec.CleanupSeconds != 60
            || spec.MinimumAvailableMemoryBytes != 4294967296
            || spec.MinimumFreeDiskBytes != 5368709120
            || spec.MaximumEvidenceBytes != 536870912
            || !spec.DatabaseImage.StartsWith("pgvector/pgvector@sha256:", StringComparison.Ordinal)
            || spec.DatabaseImage["pgvector/pgvector@sha256:".Length..].Length != 64
            || !spec.DatabaseImage["pgvector/pgvector@sha256:".Length..].All(char.IsAsciiHexDigit)
            || !spec.DatabaseImageId.StartsWith("sha256:", StringComparison.Ordinal)
            || spec.DatabaseImageId.Length != 71
            || !spec.DatabaseImageId[7..].All(char.IsAsciiHexDigit)
        )
            throw new InvalidDataException("Frozen M2 profile bounds changed.");
        Resolve(root, spec.Output, true);
        Resolve(root, spec.Manifest, false);
        foreach (var input in spec.Inputs)
            Resolve(root, input, false, allowChange: true);
        if (
            !spec.Inputs.Contains(
                Path.GetRelativePath(root, source).Replace('\\', '/'),
                StringComparer.Ordinal
            )
        )
            throw new InvalidDataException("Profile is not in its manifest inputs.");
        HotelsSourceManifest.VerifyFile(root, Resolve(root, spec.Manifest, false), spec.Inputs);
        if (!spec.Inputs.Contains(spec.BuildInventory, StringComparer.Ordinal))
            throw new InvalidDataException("Build inventory is not frozen.");
        spec.VerifyBuild(root);
        return spec;
    }

    internal void VerifyBuild(string root) =>
        HotelM2BuildInventory.Verify(root, Resolve(root, BuildInventory, false));

    internal static string Resolve(
        string root,
        string relative,
        bool mayNotExist,
        bool allowChange = false
    )
    {
        root = Path.GetFullPath(root);
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("M2 paths must be relative.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var boundary = Path.Combine(root, "TestResults", "hotels-m2-memory-load-acceptance");
        var change = Path.Combine(root, "openspec", "changes", "hotels-m2-memory-load-acceptance");
        if (!Within(boundary, full) && !(allowChange && Within(change, full)))
            throw new InvalidDataException("M2 path escapes the owned evidence tree.");
        var current = full;
        while (!File.Exists(current) && !Directory.Exists(current))
        {
            if (!mayNotExist)
                throw new FileNotFoundException("Frozen M2 input absent.", full);
            current =
                Path.GetDirectoryName(current)
                ?? throw new InvalidDataException("Path root absent.");
        }
        for (
            FileSystemInfo? entry = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            entry is not null;

        )
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Reparse points are not allowed in a frozen M2 path."
                );
            if (string.Equals(entry.FullName, root, StringComparison.OrdinalIgnoreCase))
                break;
            entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent;
        }
        return full;
    }

    private static bool Within(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
