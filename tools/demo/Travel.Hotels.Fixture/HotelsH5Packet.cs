using System.Text.Json;
using Microsoft.Win32;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH5Packet(
    int Version,
    string LoadProfile,
    string Workload,
    string Vmmap,
    string VmmapSha256
)
{
    internal const string ToolHash =
        "0B3731C524E6BA716F15087D85EAE7E6225B6B51D4AE2FA6C142FF1523F57046";

    internal static (HotelH5Packet Packet, HotelM2LoadRunSpec Spec) Read(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        var packet =
            JsonSerializer.Deserialize<HotelH5Packet>(
                File.ReadAllText(HotelM2LoadRunSpec.Resolve(root, relative, false)),
                HotelM2LoadRunSpec.Json
            ) ?? throw new InvalidDataException("H5 packet absent.");
        var spec = HotelM2LoadRunSpec.Read(
            root,
            HotelM2LoadRunSpec.Resolve(root, packet.LoadProfile, false)
        );
        if (
            packet.Version != 1
            || spec.Code != "S"
            || packet.VmmapSha256 != ToolHash
            || !spec.Inputs.Contains(relative, StringComparer.Ordinal)
            || !spec.Inputs.Contains(packet.Workload, StringComparer.Ordinal)
            || !spec.Output.EndsWith("/run-01", StringComparison.Ordinal)
            || Path.GetDirectoryName(spec.Output) != Path.GetDirectoryName(relative)
        )
            throw new InvalidDataException("H5 frozen protocol paths changed.");
        var jobs = JsonSerializer.Deserialize<HotelM2PlannedJob[]>(
            File.ReadAllText(HotelM2LoadRunSpec.Resolve(root, packet.Workload, false)),
            HotelM2LoadRunSpec.Json
        );
        if (jobs is null || !jobs.SequenceEqual(HotelsH5Controller.Prefix(spec.Profile)))
            throw new InvalidDataException("H5 exact warmup prefix changed.");
        packet.VerifyTool(root);
        if (
            !OperatingSystem.IsWindows()
            || spec.CheckIn <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        )
            throw new InvalidDataException("H5 Windows/dates unavailable; prepare a new packet.");
        return (packet, spec);
    }

    internal void VerifyTool(string root)
    {
        if (
            VmmapSha256 != ToolHash
            || HotelsL1HostProcess.Hash(HotelM2LoadRunSpec.Resolve(root, Vmmap, false)) != ToolHash
        )
            throw new InvalidDataException("Pinned VMMap bytes changed.");
    }

    internal static bool EulaAccepted()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Sysinternals\VMMap",
            writable: false
        );
        return key?.GetValue("EulaAccepted") is int value && value == 1;
    }
}
