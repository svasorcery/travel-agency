using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public sealed class TripsBriefEvaluationFileJournal : ITripsBriefEvaluationJournal
{
    private const int RecordLimit = 64 * 1024;
    private const long DirectoryLimit = 256L * 1024 * 1024;
    private readonly TripsBriefEvaluationBinding binding;
    private readonly string root;
    private bool claimed;
    private bool runStarted;

    public void BeginRun(TripsBriefEvaluationBinding value, TimeProvider time)
    {
        if (runStarted)
            throw new InvalidOperationException("AuthorizationAlreadyUsed");
        ClaimAuthorization(value, time);
        runStarted = true;
    }

    public void ClaimAuthorization(TimeProvider time) => ClaimAuthorization(binding, time);

    public void ClaimAuthorization(TripsBriefEvaluationBinding value, TimeProvider time)
    {
        if (value != binding)
            throw new InvalidOperationException("ClaimBindingChanged");
        if (claimed)
            return;
        TripsBriefEvaluationRunner.ClaimAuthorization(binding, time);
        claimed = true;
    }

    public string? CompletedIndexSha256 { get; private set; }
    public string EvidenceKind => "durable-fictional-component-pipeline";
    public string ManifestSha256 { get; }
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private sealed record Reference(string Name, string Sha256);

    private sealed record Index(TripsBriefEvaluationReceipt Aggregate, Reference[] Cases);

    public TripsBriefEvaluationFileJournal(
        TripsBriefEvaluationBinding binding,
        string manifestSha256
    )
    {
        this.binding = binding;
        if (!TripsBriefEvaluationAuthorization.Digest(manifestSha256))
            throw new InvalidOperationException("ManifestIdentityMissing");
        ManifestSha256 = manifestSha256;
        root = CheckRoot(binding.RunReceiptDirectory!);
        Directory.CreateDirectory(root);
        CheckRoot(root);
    }

    public static string CheckRoot(string root)
    {
        if (
            string.IsNullOrEmpty(root)
            || !Path.IsPathFullyQualified(root)
            || root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("..")
        )
            throw new InvalidOperationException("StableEvidenceRootRequired");
        var full = Path.GetFullPath(root);
        for (var d = new DirectoryInfo(full); d is not null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("LinkedEvidenceRootRejected");
        return full;
    }

    private string Name(string id, string suffix)
    {
        if (
            id.Length != 3
            || id[0] is not ('D' or 'H' or 'E')
            || !int.TryParse(id.AsSpan(1), out var n)
            || n is < 1 or > 24
        )
            throw new InvalidOperationException("CaseIdentityRejected");
        return binding.RunId.ToString("N") + "-" + id + "-" + suffix + ".json";
    }

    public Task RecordAttemptAsync(TripsBriefEvalAttempt attempt, CancellationToken ct)
    {
        if (
            attempt.RunId != binding.RunId
            || attempt.CorrelationId == Guid.Empty
            || attempt.ManifestSha256 != ManifestSha256
            || attempt.ReservationUsd is <= 0
            || attempt.ReservationUsd > binding.MaxRequestCostUsd
        )
            throw new InvalidOperationException("AttemptBindingRejected");
        Write(Name(attempt.CaseId, "intent"), attempt, ct);
        return Task.CompletedTask;
    }

    public Task RecordResultAsync(TripsBriefEvalCaseReceipt result, CancellationToken ct)
    {
        ValidateOutcome(result);
        Write(Name(result.Id, "result"), result, ct);
        return Task.CompletedTask;
    }

    internal static void ValidateOutcome(TripsBriefEvalCaseReceipt result)
    {
        // Status is controlled, never an exception or provider diagnostic string.
        if (
            !new[]
            {
                "ProcessingPolicyChanged",
                "InputUnavailable",
                "BudgetExceeded",
                "Busy",
                "InferenceFailed",
                "Refused",
                "NotRun",
                "Ready",
                "InvalidOutput",
                "UnknownAccounting",
                "TransportOrDeadlineFailure",
                "ComponentDeadlineFailure",
                "Disabled",
                "ModelUnavailable",
                "AdmissionUnavailable",
                "InputTooLarge",
                "DeadlineExceeded",
                "ProviderFailure",
                "AccountingUnavailable",
                "InvalidResponse",
            }.Contains(result.Status)
        )
            throw new InvalidOperationException("UncontrolledOutcomeRejected");
    }

    public async Task CompleteAsync(TripsBriefEvaluationReceipt receipt, CancellationToken ct)
    {
        receipt = receipt with { Passed = false, CompletionState = "FinalizationPending" };
        if (
            receipt.Binding != binding
            || receipt.Cases.Length is < 1 or > 48
            || receipt.Cases.Select(c => c.Id).Distinct().Count() != receipt.Cases.Length
        )
            throw new InvalidOperationException("ReceiptBindingRejected");
        var refs = new List<Reference>();
        foreach (var c in receipt.Cases)
        {
            var name = Name(c.Id, "result");
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
                await RecordResultAsync(c, ct);
            else if (
                !File.ReadAllBytes(path).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(c, Json))
            )
                throw new InvalidOperationException("ResultChanged");
            refs.Add(new(name, Hash(File.ReadAllBytes(path))));
        }
        var index = new Index(receipt with { Cases = [] }, refs.ToArray());
        Write(binding.RunId.ToString("N") + "-summary.json", index, ct);
        CompletedIndexSha256 = Hash(JsonSerializer.SerializeToUtf8Bytes(index, Json));
    }

    private void Write<T>(string name, T value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CheckRoot(root);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (
            bytes.Length > RecordLimit
            || Directory.EnumerateFiles(root).Sum(p => new FileInfo(p).Length) + bytes.Length
                > DirectoryLimit
        )
            throw new IOException("EvidenceLimitExceeded");
        var tmp = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (
                var stream = new FileStream(
                    tmp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough
                )
            )
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            CheckRoot(root);
            File.Move(tmp, Path.Combine(root, name), false);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    public static TripsBriefEvaluationJournalRead Read(string directory, Guid runId)
    {
        var root = CheckRoot(directory);
        var prefix = runId.ToString("N") + "-";
        byte[] ReadBytes(string name)
        {
            var path = Path.Combine(root, name);
            if (
                name != Path.GetFileName(name)
                || new FileInfo(path).Length > RecordLimit
                || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
            )
                throw new InvalidOperationException("InvalidEvidenceFile");
            return File.ReadAllBytes(path);
        }
        var intents = Directory
            .GetFiles(root, prefix + "*-intent.json")
            .Select(p =>
                JsonSerializer.Deserialize<TripsBriefEvalAttempt>(
                    ReadBytes(Path.GetFileName(p)),
                    Json
                )!
            )
            .ToArray();
        var uncertain = intents
            .Where(a => !File.Exists(Path.Combine(root, prefix + a.CaseId + "-result.json")))
            .Select(a => a.CaseId)
            .ToArray();
        var summary = prefix + "summary.json";
        if (!File.Exists(Path.Combine(root, summary)))
            return new(null, uncertain);
        var indexBytes = ReadBytes(summary);
        var index = JsonSerializer.Deserialize<Index>(indexBytes, Json)!;
        if (
            index.Cases.Length is < 1 or > 48
            || index.Aggregate.Binding.RunId != runId
            || index.Cases.Select(x => x.Name).Distinct().Count() != index.Cases.Length
        )
            throw new InvalidOperationException("InvalidEvidenceIndex");
        var cases = index
            .Cases.Select(r =>
            {
                var bytes = ReadBytes(r.Name);
                if (!r.Name.StartsWith(prefix, StringComparison.Ordinal) || Hash(bytes) != r.Sha256)
                    throw new InvalidOperationException("EvidenceHashChanged");
                return JsonSerializer.Deserialize<TripsBriefEvalCaseReceipt>(bytes, Json)!;
            })
            .ToArray();
        if (index.Aggregate.Passed || index.Aggregate.CompletionState != "FinalizationPending")
            throw new InvalidOperationException("IndexCannotClaimTerminalVerdict");
        return new(index.Aggregate with { Cases = cases }, uncertain, Hash(indexBytes));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
