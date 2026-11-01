using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Session(
    string Name,
    uint EventsLost,
    uint LogBuffersLost,
    uint RealTimeBuffersLost
);

internal static class HotelsH6Native
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint QueryAllTracesW(
        [In, Out] nint[] properties,
        uint count,
        out uint actual
    );

    // EVENT_TRACE_PROPERTIES x64: WNODE_HEADER=48, HANDLE at104, two offsets at112/116.
    internal static HotelH6Session[] Sessions()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException();
        const int size = 120 + 4096 + 4096;
        var pointers = new nint[128];
        try
        {
            for (var i = 0; i < pointers.Length; i++)
            {
                pointers[i] = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[size], 0, pointers[i], size);
                Marshal.WriteInt32(pointers[i], size);
                Marshal.WriteInt32(pointers[i], 112, 120 + 4096);
                Marshal.WriteInt32(pointers[i], 116, 120);
            }
            var status = QueryAllTracesW(pointers, (uint)pointers.Length, out var count);
            if (status != 0 || count > pointers.Length)
                throw new Win32Exception((int)status);
            return pointers
                .Take((int)count)
                .Select(p =>
                {
                    var offset = Marshal.ReadInt32(p, 116);
                    if (offset < 120 || offset > size - 4096)
                        throw new InvalidDataException("ETW session schema.");
                    var text = Marshal.PtrToStringUni(p + offset, 2048)!;
                    var end = text.IndexOf('\0');
                    if (end < 0 || end > 1024)
                        throw new InvalidDataException("ETW name bound.");
                    return new HotelH6Session(
                        text[..end],
                        (uint)Marshal.ReadInt32(p, 88),
                        (uint)Marshal.ReadInt32(p, 96),
                        (uint)Marshal.ReadInt32(p, 100)
                    );
                })
                .ToArray();
        }
        finally
        {
            foreach (var p in pointers)
                if (p != 0)
                    Marshal.FreeHGlobal(p);
        }
    }

    internal static HotelH6SessionStatus OwnSession(
        IEnumerable<HotelH6Session> sessions,
        string instance
    )
    {
        var matches = sessions
            .Where(s => s.Name.Contains(instance, StringComparison.Ordinal))
            .ToArray();
        if (matches.Any(s => !s.Name.StartsWith("WPR_initiated_", StringComparison.Ordinal)))
            return HotelH6SessionStatus.Unknown;
        return matches.Length == 0 ? HotelH6SessionStatus.Absent : HotelH6SessionStatus.Owned;
    }
}

// Privileged output never points at the user-writable repository. Files are read/copied by the
// ordinary driver, then removed by this helper only after a content fingerprint acknowledgement.
internal sealed class HotelsH6Store : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public int Length;
        public nint Descriptor;
        public int Inherit;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, ref Attributes attributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint access,
        uint share,
        nint security,
        uint disposition,
        uint flags,
        nint template
    );

    private SafeFileHandle? directory;
    internal string Root { get; }

    private HotelsH6Store(string root, SafeFileHandle handle)
    {
        Root = root;
        directory = handle;
    }

    internal static string Location(string nonce)
    {
        if (!Guid.TryParseExact(nonce, "N", out _))
            throw new InvalidDataException("H6 nonce.");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Temp",
            "TravelHotelsH6-" + nonce
        );
    }

    internal static DirectorySecurity Security(string sid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (
            var identity in new[]
            {
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            }
        )
            acl.AddAccessRule(
                new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                )
            );
        acl.AddAccessRule(
            new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow
            )
        );
        return acl;
    }

    internal static HotelsH6Store Create(string nonce, string sid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (!HotelsH6Windows.IsAdministrator())
            throw new InvalidOperationException("Elevated helper only.");
        var root = Location(nonce);
        NoLinks(Path.GetDirectoryName(root)!);
        var descriptor = Security(sid).GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new Attributes
            {
                Length = Marshal.SizeOf<Attributes>(),
                Descriptor = pinned.AddrOfPinnedObject(),
            };
            if (!CreateDirectoryW(root, ref attributes))
                throw new Win32Exception();
        }
        finally
        {
            pinned.Free();
        }
        var handle = CreateFileW(root, 0x80000000, 3, 0, 3, 0x02000000, 0); // no FILE_SHARE_DELETE
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception();
        }
        return new(root, handle);
    }

    internal static void NoLinks(string path)
    {
        for (
            var current = new DirectoryInfo(Path.GetFullPath(path));
            current is not null;
            current = current.Parent
        )
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("H6 reparse point.");
    }

    internal static string[] Files(string root)
    {
        NoLinks(root);
        var files = new List<string>();
        void Visit(string folder)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(folder))
            {
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("H6 reparse entry.");
                if (Directory.Exists(item))
                    Visit(item);
                else
                    files.Add(item);
                if (files.Count > 2048)
                    throw new InvalidDataException("H6 file bound.");
            }
        }
        Visit(root);
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    internal static long Bytes(string root) =>
        Directory.Exists(root) ? Files(root).Sum(p => new FileInfo(p).Length) : 0;

    internal static string Fingerprint(string root)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Files(root))
        {
            using var input = File.OpenRead(path);
            var name = System.Text.Encoding.UTF8.GetBytes(
                Path.GetRelativePath(root, path).Replace('\\', '/')
            );
            digest.AppendData(BitConverter.GetBytes(name.Length));
            digest.AppendData(name);
            digest.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    internal void Release(string fingerprint)
    {
        if (Fingerprint(Root) != fingerprint)
            throw new InvalidDataException("H6 handoff fingerprint changed.");
        var files = Files(Root);
        directory!.Dispose();
        directory = null;
        foreach (var file in files)
            File.Delete(file);
        // Root is a verified private directory under Windows/Temp, created by this object.
        foreach (
            var sub in Directory
                .GetDirectories(Root, "*", SearchOption.AllDirectories)
                .OrderByDescending(p => p.Length)
        )
        {
            NoLinks(sub);
            Directory.Delete(sub, false);
        }
        Directory.Delete(Root, false);
    }

    public void Dispose()
    {
        directory?.Dispose();
        directory = null;
    }
}
