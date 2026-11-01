using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Message(
    int Sequence,
    string Operation,
    HotelH6Target? Target = null,
    string? Fingerprint = null
);

internal sealed record HotelH6Reply(
    int Sequence,
    bool Ok,
    JsonElement? Data = null,
    string? Error = null
);

internal static class HotelsH6Wire
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    internal static async Task WriteAsync(Stream stream, object value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length is < 1 or > 4096)
            throw new InvalidDataException("H6 frame bound.");
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct);
        var count = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (count is < 1 or > 4096)
            throw new InvalidDataException("H6 frame bound.");
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, Json)
            ?? throw new InvalidDataException("H6 frame absent.");
    }

    internal static NamedPipeServerStream Server(string name, string sid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(sid),
                PipeAccessRights.FullControl,
                AccessControlType.Allow
            )
        );
        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow
            )
        );
        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            8192,
            8192,
            security
        );
    }

    internal static NamedPipeClientStream Client(string name)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        // CurrentUserOnly rejects different elevation levels. Explicit ACL + OS peer PID checks are
        // used instead. Identification prevents the normal server impersonating the elevated client.
        return new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification
        );
    }
}
