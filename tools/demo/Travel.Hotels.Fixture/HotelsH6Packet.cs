using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Tool(string Path, string Sha256);

internal sealed record HotelH6Packet(
    int Version,
    string H5Packet,
    string WpaVersion,
    HotelH6Tool[] Analyzer
)
{
    internal const string RecorderHash =
        "8C3D49A83EBD8138FFA15DA93D25BA1FC44ACDEC3CB9D729B6F43B55A5EF7CA5";

    internal static (HotelH6Packet Packet, HotelH5Packet H5, HotelM2LoadRunSpec Spec) Read(
        string root,
        string path
    )
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        var packet =
            JsonSerializer.Deserialize<HotelH6Packet>(
                File.ReadAllText(HotelM2LoadRunSpec.Resolve(root, relative, false)),
                HotelM2LoadRunSpec.Json
            ) ?? throw new InvalidDataException("H6 packet absent.");
        var (h5, spec) = HotelH5Packet.Read(
            root,
            HotelM2LoadRunSpec.Resolve(root, packet.H5Packet, false)
        );
        if (
            packet.Version != 1
            || !spec.Inputs.Contains(relative, StringComparer.Ordinal)
            || Path.GetDirectoryName(relative) != Path.GetDirectoryName(packet.H5Packet)
        )
            throw new InvalidDataException("H6 packet identity.");
        packet.VerifyTools();
        return (packet, h5, spec);
    }

    internal void VerifyRecorder()
    {
        if (
            !OperatingSystem.IsWindows()
            || !Environment.Is64BitProcess
            || HotelsL1HostProcess.Hash(HotelsH6Wpr.Wpr) != RecorderHash
        )
            throw new InvalidDataException("H6 recorder changed.");
    }

    internal void VerifyTools()
    {
        VerifyRecorder();
        if (
            WpaVersion != "11.8.423.0"
            || Analyzer.Length != 3
            || !Analyzer
                .Select(t => Path.GetFileName(t.Path))
                .Order()
                .SequenceEqual(
                    new[] { "perf_heapsnapshot.dll", "wpa.exe", "wpaexporter.exe" }.Order(),
                    StringComparer.OrdinalIgnoreCase
                )
        )
            throw new InvalidDataException("H6 analyzer inventory.");
        var boundary =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps",
                "Microsoft.WindowsPerformanceAnalyzer_" + WpaVersion + "_x64__8wekyb3d8bbwe"
            ) + Path.DirectorySeparatorChar;
        foreach (var tool in Analyzer)
            if (
                !Path.GetFullPath(tool.Path)
                    .StartsWith(boundary, StringComparison.OrdinalIgnoreCase)
                || tool.Sha256.Length != 64
                || !tool.Sha256.All(char.IsAsciiHexDigit)
                || HotelsL1HostProcess.Hash(tool.Path) != tool.Sha256
            )
                throw new InvalidDataException("H6 analyzer bytes changed.");
    }
}
