using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

// Fixed-size persistent bind file: bounded mmap, RLIMIT_FSIZE and readonly parent are kernel constraints.
// Each slot has payload/hash first, then a separately flushed commit marker. Never overwrite/resume.
public sealed class TripsBriefEvaluationArchiveJournal : ITripsBriefEvaluationJournal, IDisposable
{
    private const int SlotBytes = 128 * 1024;
    private const int MaxPayload = 64 * 1024;
    private const long Committed = 0x31504d4f434c5645;
    private readonly FileStream file;
    private readonly MemoryMappedFile map;
    private readonly MemoryMappedViewAccessor view;
    private readonly Header header;
    private bool started;

    private sealed record Header(
        TripsBriefEvaluationManifest Manifest,
        string ManifestSha256,
        bool ProbeOnly
    );

    private sealed record Index(
        TripsBriefEvaluationReceipt Aggregate,
        Dictionary<string, string> ResultHashes
    );

    public string EvidenceKind =>
        header.ProbeOnly ? "owned-envelope-mechanical-only" : "owned-envelope-fictional-component";
    public string ManifestSha256 => header.ManifestSha256;
    public string? CompletedIndexSha256 { get; private set; }

    public static string Create(TripsBriefEvaluationManifest manifest, string hash, bool probeOnly)
    {
        if (!TripsBriefEvaluationAuthorization.Digest(hash))
            throw new InvalidOperationException("ManifestIdentityMissing");
        var root = TripsBriefEvaluationFileJournal.CheckRoot(manifest.Binding.RunReceiptDirectory!);
        Directory.CreateDirectory(root);
        var id = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(manifest.Binding.AuthorizationId))
        );
        var path = Path.Combine(root, "trips-brief-authorization-" + id + ".claim");
        if (File.Exists(Path.Combine(root, "trips-brief-authorization-" + id + ".json")))
            throw new IOException("LegacyAuthorityConsumed");
        using (
            var f = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
        )
        {
            f.SetLength(TripsBriefEvaluationResourcePolicy.ArchiveBytes);
            f.Flush(true);
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupWrite
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherWrite
            );
        using var journal = new TripsBriefEvaluationArchiveJournal(
            path,
            new(manifest, hash, probeOnly)
        );
        journal.Write(0, journal.header);
        return path;
    }

    private TripsBriefEvaluationArchiveJournal(string path, Header expected)
    {
        if (
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
            || new FileInfo(path).Length != TripsBriefEvaluationResourcePolicy.ArchiveBytes
        )
            throw new InvalidOperationException("ArchiveShapeRejected");
        header = expected;
        file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        map = MemoryMappedFile.CreateFromFile(
            file,
            null,
            0,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            true
        );
        view = map.CreateViewAccessor(
            0,
            TripsBriefEvaluationResourcePolicy.ArchiveBytes,
            MemoryMappedFileAccess.ReadWrite
        );
    }

    public static TripsBriefEvaluationArchiveJournal Open(
        string path,
        TripsBriefEvaluationManifest m,
        string hash,
        bool probeOnly,
        string? approvedCanonicalSha256 = null
    )
    {
        var actual = ReadHeader(path);
        if (
            CanonicalManifestSha256(actual.Manifest)
                != (approvedCanonicalSha256 ?? CanonicalManifestSha256(m))
            || actual.ManifestSha256 != hash
            || actual.ProbeOnly != probeOnly
            || JsonSerializer.Serialize(actual.Manifest) != JsonSerializer.Serialize(m)
        )
            throw new InvalidOperationException("ArchiveAuthorityChanged");
        return new(path, actual);
    }

    public static string CanonicalManifestSha256(TripsBriefEvaluationManifest manifest) =>
        Hash(JsonSerializer.SerializeToUtf8Bytes(manifest));

    public static TripsBriefEvaluationManifest Manifest(string path) => ReadHeader(path).Manifest;

    public static bool ProbeOnly(string path) => ReadHeader(path).ProbeOnly;

    private static Header ReadHeader(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return JsonSerializer.Deserialize<Header>(
            ReadSlot(f, 0) ?? throw new InvalidOperationException("ArchiveAuthorityUncertain")
        )!;
    }

    public bool HasStableReceiptRoot(string? root) =>
        root == header.Manifest.Binding.RunReceiptDirectory;

    public void BeginRun(TripsBriefEvaluationBinding binding, TimeProvider time)
    {
        if (
            started
            || JsonSerializer.Serialize(binding)
                != JsonSerializer.Serialize(header.Manifest.Binding)
        )
            throw new InvalidOperationException("ArchiveRunAlreadyUsedOrChanged");
        // A stopped/uncertain previous worker cannot replay even if the header is unchanged.
        Write(100, new { binding.RunId, BeganAtUtc = time.GetUtcNow() });
        started = true;
    }

    public Task RecordAttemptAsync(TripsBriefEvalAttempt a, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (
            a.RunId != header.Manifest.Binding.RunId
            || a.ManifestSha256 != ManifestSha256
            || a.CorrelationId == Guid.Empty
            || a.ReservationUsd <= 0
            || a.ReservationUsd > header.Manifest.Binding.MaxRequestCostUsd
        )
            throw new InvalidOperationException("AttemptBindingRejected");
        Write(1 + Ordinal(a.CaseId) * 2, a);
        return Task.CompletedTask;
    }

    public Task RecordResultAsync(TripsBriefEvalCaseReceipt r, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        TripsBriefEvaluationFileJournal.ValidateOutcome(r);
        Write(2 + Ordinal(r.Id) * 2, r);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(TripsBriefEvaluationReceipt r, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (
            JsonSerializer.Serialize(r.Binding) != JsonSerializer.Serialize(header.Manifest.Binding)
            || r.Cases.Length is < 1 or > 48
            || r.Cases.Select(c => c.Id).Distinct().Count() != r.Cases.Length
        )
            throw new InvalidOperationException("ReceiptBindingRejected");
        var hashes = new Dictionary<string, string>();
        foreach (var c in r.Cases)
        {
            TripsBriefEvaluationFileJournal.ValidateOutcome(c);
            var slot = 2 + Ordinal(c.Id) * 2;
            if (view.ReadInt64(slot * (long)SlotBytes) != Committed)
                Write(slot, c);
            var bytes =
                ReadSlot(file, slot)
                ?? throw new InvalidOperationException("ArchiveResultUncertain");
            if (!bytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(c)))
                throw new InvalidOperationException("ArchiveResultChanged");
            hashes.Add(c.Id, Hash(bytes));
        }
        var index = new Index(
            r with
            {
                Cases = [],
                Passed = false,
                CompletionState = "FinalizationPending",
            },
            hashes
        );
        CompletedIndexSha256 = Write(99, index);
        return Task.CompletedTask;
    }

    private string Write<T>(int slot, T value)
    {
        var offset = checked(slot * (long)SlotBytes);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (slot is < 0 or > 100 || payload.Length > MaxPayload)
            throw new IOException("ArchiveRecordLimit");
        var existing = new byte[SlotBytes];
        view.ReadArray(offset, existing, 0, existing.Length);
        if (existing.Any(b => b != 0))
            throw new IOException("ArchiveSlotConsumedNoResume");
        var digest = SHA256.HashData(payload);
        view.WriteArray(offset + 64, payload, 0, payload.Length);
        view.Write(offset + 8, payload.Length);
        view.WriteArray(offset + 16, digest, 0, digest.Length);
        view.Flush();
        file.Flush(true);
        view.Write(offset, Committed);
        view.Flush();
        file.Flush(true);
        return Convert.ToHexStringLower(digest);
    }

    private static byte[]? ReadSlot(FileStream file, int slot)
    {
        var head = new byte[64];
        file.Position = slot * (long)SlotBytes;
        file.ReadExactly(head);
        if (BitConverter.ToInt64(head, 0) != Committed)
            return null;
        var n = BitConverter.ToInt32(head, 8);
        if (n is < 1 or > MaxPayload)
            throw new InvalidOperationException("ArchiveLengthRejected");
        var bytes = new byte[n];
        file.ReadExactly(bytes);
        if (!SHA256.HashData(bytes).SequenceEqual(head.AsSpan(16, 32).ToArray()))
            throw new InvalidOperationException("ArchiveHashChanged");
        return bytes;
    }

    public static TripsBriefEvaluationJournalRead Read(string path)
    {
        var h = ReadHeader(path);
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var uncertain = new List<string>();
        var cases = new Dictionary<string, TripsBriefEvalCaseReceipt>();
        for (var i = 0; i < 48; i++)
        {
            var intent = ReadSlot(f, 1 + i * 2);
            var result = ReadSlot(f, 2 + i * 2);
            var id = (i < 24 ? "D" : "H") + (i % 24 + 1).ToString("D2");
            if (result is not null)
                cases.Add(id, JsonSerializer.Deserialize<TripsBriefEvalCaseReceipt>(result)!);
            else if (intent is not null)
                uncertain.Add(id);
            else
            {
                f.Position = (1 + i * 2) * (long)SlotBytes;
                var raw = new byte[2 * SlotBytes];
                f.ReadExactly(raw);
                if (raw.Any(b => b != 0))
                    uncertain.Add(id);
            }
        }
        var bytes = ReadSlot(f, 99);
        if (bytes is null)
            return new(null, uncertain.ToArray());
        var index = JsonSerializer.Deserialize<Index>(bytes)!;
        if (
            index.Aggregate.Passed
            || index.Aggregate.CompletionState != "FinalizationPending"
            || index.Aggregate.Binding.RunId != h.Manifest.Binding.RunId
            || index.ResultHashes.Count != cases.Count
        )
            throw new InvalidOperationException("ArchiveIndexRejected");
        foreach (var p in index.ResultHashes)
            if (
                !cases.TryGetValue(p.Key, out var c)
                || Hash(JsonSerializer.SerializeToUtf8Bytes(c)) != p.Value
            )
                throw new InvalidOperationException("ArchiveIndexChanged");
        return new(
            index.Aggregate with
            {
                Cases = index.ResultHashes.Keys.Select(id => cases[id]).ToArray(),
            },
            uncertain.ToArray(),
            Hash(bytes)
        );
    }

    private static int Ordinal(string id)
    {
        if (
            id.Length != 3
            || id[0] is not ('D' or 'H')
            || !int.TryParse(id.AsSpan(1), out var n)
            || n is < 1 or > 24
        )
            throw new InvalidOperationException("ArchiveCaseRejected");
        return (id[0] == 'D' ? 0 : 24) + n - 1;
    }

    private static string Hash(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    public void Dispose()
    {
        view.Dispose();
        map.Dispose();
        file.Dispose();
    }
}
