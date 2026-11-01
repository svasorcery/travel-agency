using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH5Identity(
    int Pid,
    long StartUtcTicks,
    string Root,
    string BinaryHash,
    string ManifestHash
);

internal sealed record HotelH5Category(
    string RawType,
    long SizeBytes,
    long CommittedBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    long PrivateWorkingSetBytes
)
{
    public required string RawUseType { get; init; }
    public long ShareableWorkingSetBytes { get; init; }
    public long SharedWorkingSetBytes { get; init; }
}

internal sealed record HotelH5Map(
    int Pid,
    int Regions,
    long CommittedBytes,
    long PrivateBytes,
    long WorkingSetBytes,
    string[] Types,
    HotelH5Category[] Categories
)
{
    public int AllRegions { get; init; }
    public int LeafRegions { get; init; }
    public long LeafPrivateBytes { get; init; }
    public int ParentPrivateMismatchCount { get; init; }
    public long PageTableSizeBytes { get; init; }
}

// Snapshot of the Windows-only environment boundary. Tests supply fictional
// values while exercising the same argument/environment builder on any OS.
internal sealed record HotelH5SystemPaths(string Windows, string ProgramData, string SystemDrive)
{
    internal static HotelH5SystemPaths Read()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData
        );
        if (!Path.IsPathFullyQualified(windows) || !Path.IsPathFullyQualified(programData))
            throw new InvalidDataException("Windows system paths unavailable.");
        return new(
            windows,
            programData,
            Path.GetPathRoot(windows)!.TrimEnd(Path.DirectorySeparatorChar)
        );
    }
}

internal static class HotelsH5Map
{
    internal const long MaximumXmlBytes = 64 * 1024 * 1024;

    internal static void ValidateIdentity(
        HotelH5Identity owned,
        HotelH5Identity observed,
        bool exited
    )
    {
        if (owned.Pid <= 0 || owned.StartUtcTicks <= 0 || exited || owned != observed)
            throw new InvalidDataException("Owned live Host identity changed.");
    }

    internal static ProcessStartInfo StartInfo(
        string tool,
        int pid,
        string output,
        HotelH5SystemPaths? systemPaths = null
    )
    {
        if (
            pid <= 0
            || !Path.IsPathFullyQualified(output)
            || !output.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidDataException(
                "VMMap requires an owned numeric PID and absolute XML output."
            );
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var paths = systemPaths ?? HotelH5SystemPaths.Read();
        if (
            string.IsNullOrWhiteSpace(paths.Windows)
            || string.IsNullOrWhiteSpace(paths.ProgramData)
            || string.IsNullOrWhiteSpace(paths.SystemDrive)
        )
            throw new InvalidDataException("Windows system paths unavailable.");
        var info = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory,
        };
        info.Environment.Clear();
        info.Environment["SystemRoot"] = paths.Windows;
        info.Environment["WINDIR"] = paths.Windows;
        info.Environment["SystemDrive"] = paths.SystemDrive;
        info.Environment["ProgramData"] = paths.ProgramData;
        // Preserve only standard Windows profile locations. No arbitrary PATH, hooks, symbols or credentials.
        foreach (
            var (name, folder) in new[]
            {
                ("USERPROFILE", Environment.SpecialFolder.UserProfile),
                ("LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData),
                ("APPDATA", Environment.SpecialFolder.ApplicationData),
            }
        )
        {
            var value = Environment.GetFolderPath(folder);
            if (Path.IsPathFullyQualified(value))
                info.Environment[name] = value;
        }
        info.Environment["TEMP"] = directory;
        info.Environment["TMP"] = directory;
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(Path.GetFullPath(output));
        return info;
    }

    internal static HotelH5Map Read(string file, int pid)
    {
        using var input = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length <= 0 || input.Length > MaximumXmlBytes)
            throw new InvalidDataException("Missing/oversized native map.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
            MaxCharactersFromEntities = 0,
            CloseInput = false,
        };
        // Bound the tree before materializing or traversing it; the actual export has depth <= 6.
        using (var scan = XmlReader.Create(input, settings))
        {
            var regionCount = 0;
            while (scan.Read())
                if (
                    scan.Depth > 16
                    || scan.NodeType == XmlNodeType.Element
                        && scan.LocalName == "Region"
                        && ++regionCount > 200000
                )
                    throw new InvalidDataException("Native map hierarchy exceeds its bound.");
        }
        input.Position = 0;
        using var reader = XmlReader.Create(input, settings);
        var root =
            XDocument.Load(reader).Root ?? throw new InvalidDataException("Map root absent.");
        if (
            root.Name != "root"
            || Field(root, "Application") != "VMMAP"
            || Field(root, "Version") != "2.0"
            || Number(root, "PID") != pid
        )
            throw new InvalidDataException("Unsupported VMMap schema or wrong PID.");
        var snapshots = OnlyChild(root, "Snapshots").Elements("Snapshot").ToArray();
        if (snapshots.Length != 1)
            throw new InvalidDataException("Exactly one map snapshot required.");
        _ = Number(snapshots[0], "Timestamp");
        var pageTableSize = Number(snapshots[0], "PageTableSize");
        var container = OnlyChild(snapshots[0], "MemoryRegions");
        var top = container.Elements().ToArray();
        if (top.Length == 0 || top.Any(e => e.Name != "Region"))
            throw new InvalidDataException("Unsupported/empty map regions.");
        var all = container.Descendants("Region").ToArray();
        var values = all.ToDictionary(e => e, ParseRegion);
        var privateMismatches = 0;
        foreach (var element in all)
        {
            var children = element.Elements().ToArray();
            if (children.Any(e => e.Name != "Region"))
                throw new InvalidDataException("Unsupported nested map element.");
            if (children.Length == 0)
                continue;
            var childSum = Sum(
                children.Select(e => values[e]),
                values[element].RawType,
                values[element].RawUseType
            );
            var parent = values[element];
            if (
                parent.SizeBytes != childSum.SizeBytes
                || parent.CommittedBytes != childSum.CommittedBytes
                || parent.PrivateWorkingSetBytes != childSum.PrivateWorkingSetBytes
                || parent.ShareableWorkingSetBytes != childSum.ShareableWorkingSetBytes
                || parent.SharedWorkingSetBytes != childSum.SharedWorkingSetBytes
            )
                throw new InvalidDataException("Native map hierarchy counters disagree.");
            // Real VMMap P0 has 284 such differences. Retain both scopes; do not invent equality.
            if (parent.PrivateBytes != childSum.PrivateBytes)
                privateMismatches++;
        }
        var rows = top.Select(e => values[e]).ToArray();
        var categories = rows.GroupBy(r => (r.RawType, r.RawUseType))
            .Select(g => Sum(g, g.Key.RawType, g.Key.RawUseType))
            .ToArray();
        var leaf = all.Where(e => !e.Elements("Region").Any()).ToArray();
        return new(
            pid,
            top.Length,
            rows.Sum(r => r.CommittedBytes),
            rows.Sum(r => r.PrivateBytes),
            rows.Sum(r => r.WorkingSetBytes),
            categories.Select(c => c.RawType).Distinct().ToArray(),
            categories
        )
        {
            AllRegions = all.Length,
            LeafRegions = leaf.Length,
            LeafPrivateBytes = leaf.Sum(e => values[e].PrivateBytes),
            ParentPrivateMismatchCount = privateMismatches,
            PageTableSizeBytes = pageTableSize,
        };
    }

    private static HotelH5Category ParseRegion(XElement region)
    {
        _ = Number(region, "Address");
        _ = Number(region, "UseType");
        var type = Field(region, "Type");
        var useType = Field(region, "UseType");
        var size = Number(region, "Size");
        var commit = Number(region, "Commit");
        var privateBytes = Number(region, "PrivateBytes");
        var privateWs = Number(region, "PrivateWS");
        var shareableWs = Number(region, "ShareableWS");
        var sharedWs = Number(region, "SharedWS");
        var ws = checked(privateWs + shareableWs); // SharedWS is a subset of ShareableWS, never added again.
        if (commit > size || privateBytes > size || ws > size || sharedWs > shareableWs)
            throw new InvalidDataException("Native map counters are inconsistent.");
        return new(type, size, commit, privateBytes, ws, privateWs)
        {
            RawUseType = useType,
            ShareableWorkingSetBytes = shareableWs,
            SharedWorkingSetBytes = sharedWs,
        };
    }

    private static HotelH5Category Sum(
        IEnumerable<HotelH5Category> values,
        string type,
        string useType
    )
    {
        var rows = values.ToArray();
        return new(
            type,
            rows.Sum(r => r.SizeBytes),
            rows.Sum(r => r.CommittedBytes),
            rows.Sum(r => r.PrivateBytes),
            rows.Sum(r => r.WorkingSetBytes),
            rows.Sum(r => r.PrivateWorkingSetBytes)
        )
        {
            RawUseType = useType,
            ShareableWorkingSetBytes = rows.Sum(r => r.ShareableWorkingSetBytes),
            SharedWorkingSetBytes = rows.Sum(r => r.SharedWorkingSetBytes),
        };
    }

    private static XElement OnlyChild(XElement parent, string name)
    {
        var children = parent.Elements(name).ToArray();
        if (children.Length != 1)
            throw new InvalidDataException("Exactly one native map container required: " + name);
        return children[0];
    }

    private static string Field(XElement e, string name)
    {
        var values = e.Attributes(name)
            .Select(a => a.Value)
            .Concat(e.Elements(name).Select(c => c.Value))
            .ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0].Length > 256)
            throw new InvalidDataException("Unsupported native map field.");
        return values[0];
    }

    private static long Number(XElement e, string name)
    {
        if (
            !long.TryParse(
                Field(e, name),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var n
            )
            || n < 0
        )
            throw new InvalidDataException("Native map counter is not a non-negative integer.");
        return n;
    }

    internal static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (text.Length + count > 8192)
                throw new InvalidDataException("Collector output overflow.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
