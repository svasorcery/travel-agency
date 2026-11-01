using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Process(
    int Pid,
    long StartUtcTicks,
    int ParentPid,
    string Image,
    string UserSid,
    bool Elevated,
    string[] Arguments
);

internal static class HotelsH6Windows
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo
    {
        public nint ExitStatus,
            Peb,
            Affinity,
            Priority,
            Pid,
            Parent;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint process,
        int information,
        nint buffer,
        int length,
        out int returned
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint process,
        uint access,
        out SafeAccessTokenHandle token
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int information,
        out int value,
        int length,
        out int returned
    );

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string command, out int count);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);

    internal static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static HotelH6Process ReadProcess(int pid)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException();
        using var process = Process.GetProcessById(pid);
        var buffer = Marshal.AllocHGlobal(65536);
        try
        {
            if (
                NtQueryInformationProcess(
                    process.Handle,
                    0,
                    buffer,
                    Marshal.SizeOf<BasicInfo>(),
                    out _
                ) != 0
            )
                throw new InvalidDataException("Process parent identity unavailable.");
            var basic = Marshal.PtrToStructure<BasicInfo>(buffer);
            // ProcessCommandLineInformation is read-only. Unsupported OS behavior fails closed.
            if (NtQueryInformationProcess(process.Handle, 60, buffer, 65536, out _) != 0)
                throw new InvalidDataException("Process command identity unavailable.");
            var length = (ushort)Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, 8);
            if (
                length == 0
                || length % 2 != 0
                || text < buffer
                || text.ToInt64() + length > buffer.ToInt64() + 65536
            )
                throw new InvalidDataException("Process command buffer invalid.");
            var command = Marshal.PtrToStringUni(text, length / 2)!;
            var argv = CommandLineToArgvW(command, out var count);
            if (argv == 0)
                throw new InvalidDataException("Process arguments invalid.");
            string[] arguments;
            try
            {
                if (count is < 1 or > 64)
                    throw new InvalidDataException("Process argument count invalid.");
                arguments = Enumerable
                    .Range(0, count)
                    .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!)
                    .ToArray();
            }
            finally
            {
                LocalFree(argv);
            }
            if (!OpenProcessToken(process.Handle, 8, out var token))
                throw new Win32Exception();
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (!GetTokenInformation(token, 20, out var elevated, sizeof(int), out _))
                    throw new Win32Exception();
                return new(
                    process.Id,
                    process.StartTime.ToUniversalTime().Ticks,
                    checked((int)basic.Parent),
                    process.MainModule?.FileName
                        ?? throw new InvalidDataException("Process image unavailable."),
                    identity.User?.Value
                        ?? throw new InvalidDataException("Process owner unavailable."),
                    elevated != 0,
                    arguments
                );
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void Validate(
        HotelH6Process actual,
        int pid,
        long ticks,
        int? parent,
        string sid,
        bool elevated,
        string image,
        string[] suffix
    )
    {
        var prefix = actual.Arguments.Length - suffix.Length;
        if (
            pid <= 0
            || ticks <= 0
            || actual.Pid != pid
            || actual.StartUtcTicks != ticks
            || parent is int expectedParent && actual.ParentPid != expectedParent
            || actual.UserSid != sid
            || actual.Elevated != elevated
            || !string.Equals(
                Path.GetFullPath(actual.Image),
                Path.GetFullPath(image),
                StringComparison.OrdinalIgnoreCase
            )
            || !(
                prefix == 1
                || prefix == 3
                    && actual.Arguments[1] == "--fx-version"
                    && actual.Arguments[2] == "10.0.9"
            )
            || !actual
                .Arguments.TakeLast(suffix.Length)
                .SequenceEqual(suffix, StringComparer.OrdinalIgnoreCase)
        )
            throw new InvalidDataException("H6 process identity/role mismatch.");
    }

    internal static int ServerPid(PipeStream pipe)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid))
            throw new Win32Exception();
        return checked((int)pid);
    }

    internal static int ClientPid(PipeStream pipe)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid))
            throw new Win32Exception();
        return checked((int)pid);
    }
}
