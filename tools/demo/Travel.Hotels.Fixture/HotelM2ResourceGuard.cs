using System.Runtime.InteropServices;

namespace Travel.Hotels.Fixture;

internal static class HotelM2ResourceGuard
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length,
            Load;
        public ulong TotalPhysical,
            AvailablePhysical,
            TotalPageFile,
            AvailablePageFile,
            TotalVirtual,
            AvailableVirtual,
            AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    internal static object Check(
        HotelM2LoadRunSpec spec,
        string output,
        Action<object>? observed = null
    )
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("The recorded M2 profile requires Windows.");
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory))
            throw new InvalidOperationException("Available-memory safety observation unavailable.");
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(output))!).AvailableFreeSpace;
        long evidence = 0;
        if (Directory.Exists(output))
            foreach (
                var file in new DirectoryInfo(output).EnumerateFiles(
                    "*",
                    SearchOption.AllDirectories
                )
            )
                evidence = checked(evidence + file.Length);
        var reading = new
        {
            memory.TotalPhysical,
            memory.AvailablePhysical,
            memory.TotalPageFile,
            memory.AvailablePageFile,
            freeDiskBytes = free,
            ownEvidenceBytes = evidence,
        };
        observed?.Invoke(reading);
        if (
            memory.AvailablePhysical < (ulong)spec.MinimumAvailableMemoryBytes
            || free < spec.MinimumFreeDiskBytes
            || evidence > spec.MaximumEvidenceBytes
        )
            throw new InvalidOperationException("Predeclared M2 resource safety stop.");
        return reading;
    }
}
