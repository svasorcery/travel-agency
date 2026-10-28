using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;

namespace Travel.Trips.Evaluation;

public sealed record TripsBriefEnvelopePreparation(
    string ImageId,
    string WorkerClosure,
    string SourceKey,
    TripsBriefEvaluationArtifacts Source
);

public sealed record TripsBriefEnvelopeProbeReceipt(
    bool BudgetVerified,
    bool SharedCgroup,
    long WritableBytes,
    long ArchiveBytes,
    string ArchivePath,
    string? IndexSha256,
    int LedgerRows,
    int RealProviderCalls,
    int FakeSdkCalls,
    bool CleanupVerified,
    bool QuotaRejectionsVerified = false,
    bool LedgerLoopbackOnly = false
);

public sealed record TripsBriefWorkerAdmission(
    string ManifestSha256,
    long RemainingTicks,
    string CanonicalManifestSha256 = ""
);

// One cgroup for BOTH worker and PG. Host performs only source admission/control and retains one fixed file.
public static class TripsBriefEvaluationEnvelope
{
    private static readonly SemaphoreSlim PreparationGate = new(1);

    public static string PreparationPath(string root) =>
        Path.Combine(root, "out/eval-prep/envelope/preparation.json");

    public static string PublishPath(string root) =>
        Path.Combine(root, "out/eval-prep/envelope/context/app");

    public static async Task<TripsBriefEnvelopePreparation> PrepareAsync(
        string root,
        CancellationToken ct
    )
    {
        await PreparationGate.WaitAsync(ct);
        try
        {
            await using var inspection = new TripsBriefEvaluationEnvironment();
            var identity = inspection.InspectArtifacts(root);
            var sourceFiles = new[]
            {
                "apps/Travel.AI/TripsBriefExtraction",
                "tools/trips/Travel.Trips.Evaluation",
                "tests/Travel.Tests.AiEvals/TripsBriefExtraction",
            }
                .SelectMany(p =>
                    Directory.GetFiles(Path.Combine(root, p), "*", SearchOption.TopDirectoryOnly)
                )
                .Where(p =>
                    p.EndsWith(".cs")
                    || p.EndsWith(".csproj")
                    || p.EndsWith("Dockerfile")
                    || p.EndsWith(".sh")
                )
                .Order(StringComparer.Ordinal);
            var key = Hash(
                Encoding.UTF8.GetBytes(
                    identity.Commit
                        + identity.SourceTree
                        + identity.Clean
                        + identity.Normalizer
                        + identity.Lexicon
                        + identity.Packages
                        + string.Join(
                            '\n',
                            sourceFiles.Select(p =>
                                Path.GetRelativePath(root, p) + "\0" + Hash(File.ReadAllBytes(p))
                            )
                        )
                )
            );
            if (File.Exists(PreparationPath(root)))
            {
                var previous = JsonSerializer.Deserialize<TripsBriefEnvelopePreparation>(
                    File.ReadAllText(PreparationPath(root))
                )!;
                if (
                    previous.SourceKey == key
                    && (
                        await ProcessAsync(
                            "docker",
                            root,
                            ["image", "inspect", previous.ImageId, "--format", "{{.Id}}"],
                            ct,
                            allowFailure: true
                        )
                    ).Code == 0
                )
                    return previous;
            }
            var work = Path.GetDirectoryName(PreparationPath(root))!;
            Directory.CreateDirectory(Path.Combine(work, "context"));
            var publish = PublishPath(root);
            File.WriteAllText(Path.Combine(work, "context/.dockerignore"), "app\n");
            await ProcessAsync(
                "dotnet",
                root,
                [
                    "publish",
                    "tools/trips/Travel.Trips.Evaluation",
                    "--configuration",
                    "Release",
                    "--artifacts-path",
                    Path.Combine(work, "build"),
                    "--output",
                    publish,
                    "--no-self-contained",
                    "--disable-build-servers",
                    "-p:UseAppHost=false",
                    "--verbosity",
                    "quiet",
                ],
                ct,
                timeoutSeconds: 180
            );
            File.Copy(
                Path.Combine(root, "tools/trips/Travel.Trips.Evaluation/Envelope.Dockerfile"),
                Path.Combine(work, "context/Dockerfile"),
                true
            );
            File.Copy(
                Path.Combine(root, "tools/trips/Travel.Trips.Evaluation/envelope.sh"),
                Path.Combine(work, "context/envelope.sh"),
                true
            );
            File.Copy(
                Path.Combine(root, TripsBriefEvaluationCommand.PacketRelativePath),
                Path.Combine(publish, "corpus.json"),
                true
            );
            var source = identity with { WorkerClosure = "", WorkerImage = "" };
            File.WriteAllText(
                Path.Combine(publish, "source-identity.json"),
                JsonSerializer.Serialize(source)
            );
            var closure = Closure(publish);
            var imageKey = Hash(
                Encoding.UTF8.GetBytes(
                    File.ReadAllText(Path.Combine(work, "context/Dockerfile"))
                        + File.ReadAllText(Path.Combine(work, "context/envelope.sh"))
                )
            );
            var tag = "trips-m3-envelope-runtime:" + imageKey[..20];
            if (
                (
                    await ProcessAsync(
                        "docker",
                        root,
                        ["image", "inspect", tag, "--format", "{{.Id}}"],
                        ct,
                        allowFailure: true
                    )
                ).Code != 0
            )
                await ProcessAsync(
                    "docker",
                    root,
                    [
                        "build",
                        "--network",
                        "none",
                        "--pull=false",
                        "--label",
                        "travel.trips.source=" + imageKey,
                        "-t",
                        tag,
                        Path.Combine(work, "context"),
                    ],
                    ct,
                    timeoutSeconds: 180
                );
            var image = (
                await ProcessAsync(
                    "docker",
                    root,
                    ["image", "inspect", tag, "--format", "{{.Id}}"],
                    ct
                )
            ).Output.Trim();
            var prepared = new TripsBriefEnvelopePreparation(image, closure, key, source);
            File.WriteAllText(PreparationPath(root), JsonSerializer.Serialize(prepared));
            return prepared;
        }
        finally
        {
            PreparationGate.Release();
        }
    }

    public static string Closure(string directory)
    {
        var files = Directory
            .GetFiles(directory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal);
        return Hash(
            Encoding.UTF8.GetBytes(
                string.Join(
                    '\n',
                    files.Select(p =>
                        Path.GetRelativePath(directory, p).Replace('\\', '/')
                        + "\0"
                        + Hash(File.ReadAllBytes(p))
                    )
                )
            )
        );
    }

    private static IContainer Build(
        TripsBriefEnvelopePreparation prepared,
        string archive,
        Guid runId,
        bool probe,
        string app
    )
    {
        var builder = new ContainerBuilder(prepared.ImageId)
            .WithCleanUp(false)
            .WithEnvironment("POSTGRES_DB", "fictional_evaluation")
            .WithEnvironment("POSTGRES_USER", "fictional_evaluator")
            .WithEnvironment("POSTGRES_PASSWORD", Guid.NewGuid().ToString("N"))
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--auth-local=trust --auth-host=scram-sha-256")
            .WithLabel("travel.trips.envelope", runId.ToString("N"))
            .WithBindMount(archive, TripsBriefEvaluationResourcePolicy.ArchiveMount)
            .WithBindMount(
                Physical(app),
                "/app",
                DotNet.Testcontainers.Configurations.AccessMode.ReadOnly
            )
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted(
                        "pg_isready",
                        "-h",
                        "127.0.0.1",
                        "-U",
                        "fictional_evaluator",
                        "-d",
                        "fictional_evaluation"
                    )
            )
            .WithCreateParameterModifier(p =>
            {
                p.User = "999:999";
                p.HostConfig.NanoCPUs = TripsBriefEvaluationResourcePolicy.NanoCpus;
                p.HostConfig.Memory = TripsBriefEvaluationResourcePolicy.MaximumMemoryBytes;
                p.HostConfig.MemorySwap = p.HostConfig.Memory;
                p.HostConfig.ReadonlyRootfs = true;
                p.HostConfig.CapDrop = ["ALL"];
                p.HostConfig.SecurityOpt = ["no-new-privileges:true"];
                p.HostConfig.ShmSize = TripsBriefEvaluationResourcePolicy.TmpfsBytes[4];
                p.HostConfig.Ulimits =
                [
                    new Ulimit
                    {
                        Name = "fsize",
                        Soft = TripsBriefEvaluationResourcePolicy.ArchiveBytes,
                        Hard = TripsBriefEvaluationResourcePolicy.ArchiveBytes,
                    },
                ];
                p.HostConfig.Tmpfs = Enumerable
                    .Range(0, 4)
                    .ToDictionary(
                        i => TripsBriefEvaluationResourcePolicy.TmpfsPaths[i],
                        i =>
                            "rw,nosuid,nodev,noexec,uid=999,gid=999,size="
                            + TripsBriefEvaluationResourcePolicy.TmpfsBytes[i]
                    );
                p.HostConfig.LogConfig = new LogConfig
                {
                    Type = "none",
                    Config = new Dictionary<string, string>(),
                };
                p.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>();
                if (probe)
                    p.HostConfig.NetworkMode = "none";
            });
        return builder.Build();
    }

    public static async Task<TripsBriefEnvelopeProbeReceipt> ProbeAsync(
        string root,
        string evidenceRoot,
        bool crash,
        CancellationToken ct
    )
    {
        var prepared = await PrepareAsync(root, ct);
        var model = "fictional-model";
        var b = new TripsBriefEvaluationBinding(
            Guid.NewGuid(),
            "source-budget-" + Guid.NewGuid().ToString("N"),
            true,
            TripsBriefEvaluationPacket.HashFile(
                Path.Combine(root, TripsBriefEvaluationCommand.PacketRelativePath)
            ),
            prepared.Source.SourceTree,
            prepared.Source.Prompt,
            model,
            1,
            5,
            .03m,
            1,
            RunReceiptDirectory: evidenceRoot
        );
        var options = new TripsBriefEvaluationOptions(
            "anthropic",
            model,
            model,
            1,
            5,
            .03m,
            .30m,
            TripsBriefPolicyIdentity.Compute(
                "anthropic",
                model,
                TripsBriefMessageIdentity.PrivacyNoticeVersion
            )
        );
        var manifest = new TripsBriefEvaluationManifest(
            1,
            b,
            prepared.Source,
            options,
            "https://api.anthropic.com",
            true,
            TimeProvider.System.GetUtcNow().AddMinutes(5)
        );
        var hash = Hash(JsonSerializer.SerializeToUtf8Bytes(manifest));
        var archive = TripsBriefEvaluationArchiveJournal.Create(manifest, hash, true);
        await using var container = Build(prepared, archive, b.RunId, true, PublishPath(root));
        var clean = false;
        try
        {
            await container.StartAsync(ct);
            await VerifyHostCapsAsync(
                container,
                archive,
                b.RunId,
                prepared.ImageId,
                Physical(PublishPath(root)),
                ct
            );
            var result = await RunWorkerAsync(
                container,
                crash ? "__worker-probe-crash" : "__worker-probe",
                () =>
                    new(
                        hash,
                        TimeSpan.FromMinutes(4).Ticks,
                        TripsBriefEvaluationArchiveJournal.CanonicalManifestSha256(manifest)
                    ),
                null,
                ct
            );
            if (crash)
            {
                if (result.Code != 86)
                    throw new InvalidOperationException("ProbeCrashWasNotObserved");
                return new(
                    true,
                    true,
                    256L * 1024 * 1024,
                    new FileInfo(archive).Length,
                    archive,
                    null,
                    0,
                    0,
                    0,
                    true
                );
            }
            if (result.Code != 0)
            {
                var memory = await ProcessAsync(
                    "docker",
                    root,
                    [
                        "exec",
                        container.Id,
                        "sh",
                        "-c",
                        "cat /sys/fs/cgroup/memory.peak /sys/fs/cgroup/memory.events; df -k /tmp /var/lib/postgresql/data",
                    ],
                    ct
                );
                throw new InvalidOperationException(
                    "EnvelopeProbeFailed:" + result.Output + ":" + memory.Output
                );
            }
            var receipt = JsonSerializer.Deserialize<TripsBriefEnvelopeProbeReceipt>(
                result.Output.Trim()
            )!;
            return receipt with { ArchivePath = archive, CleanupVerified = true };
        }
        finally
        {
            clean = await RemoveOwnedAsync(container, b.RunId, ct);
            if (!clean)
                throw new InvalidOperationException("OwnedEnvelopeCleanupUnproven");
        }
    }

    public static async Task<TripsBriefEnvelopeOutcome> ExecuteAsync(
        string root,
        TripsBriefEvaluationManifest manifest,
        string hash,
        TripsBriefEvaluationAuthorizationClock authority,
        Func<bool> stillApproved,
        CancellationToken ct
    )
    {
        var prepared = JsonSerializer.Deserialize<TripsBriefEnvelopePreparation>(
            File.ReadAllText(PreparationPath(root))
        )!;
        if (
            prepared.ImageId != manifest.ExpectedArtifacts.WorkerImage
            || Closure(PublishPath(root)) != manifest.ExpectedArtifacts.WorkerClosure
            || !stillApproved()
        )
            throw new InvalidOperationException("PreparedWorkerChanged");
        var archive = TripsBriefEvaluationArchiveJournal.Create(manifest, hash, false);
        await using var container = Build(
            prepared,
            archive,
            manifest.Binding.RunId,
            false,
            PublishPath(root)
        );
        try
        {
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(ct);
            setup.CancelAfter(TimeSpan.FromMinutes(2));
            await container.StartAsync(setup.Token);
            await VerifyHostCapsAsync(
                container,
                archive,
                manifest.Binding.RunId,
                prepared.ImageId,
                Physical(PublishPath(root)),
                ct
            );
            var result = await RunWorkerAsync(
                container,
                "__worker-execute",
                () =>
                {
                    if (!stillApproved())
                        throw new InvalidOperationException("SourceChangedBeforeWorker");
                    return new(
                        hash,
                        Math.Min(
                            TimeSpan.FromHours(6).Ticks,
                            authority.Remaining(manifest.AuthorizationExpiresAtUtc).Ticks
                        ),
                        TripsBriefEvaluationArchiveJournal.CanonicalManifestSha256(manifest)
                    );
                },
                () =>
                {
                    if (
                        !stillApproved()
                        || authority.Remaining(manifest.AuthorizationExpiresAtUtc)
                            <= TimeSpan.FromSeconds(5)
                    )
                        throw new InvalidOperationException("SourceOrAuthorityChangedBeforeKey");
                    return System.Environment.GetEnvironmentVariable(
                            "TRIPS_EVALUATION_ANTHROPIC_KEY"
                        ) ?? throw new InvalidOperationException("ExplicitEvaluationKeyMissing");
                },
                ct
            );
            if (result.Code is not (0 or 3))
                throw new InvalidOperationException("WorkerCompletionUncertainNoRetry");
            var outcome =
                JsonSerializer.Deserialize<TripsBriefEnvelopeOutcome>(result.Output.Trim())
                ?? throw new InvalidOperationException("WorkerCompletionMissing");
            var evidence = TripsBriefEvaluationArchiveJournal.Read(archive);
            if (
                outcome.RunId != manifest.Binding.RunId
                || outcome.IndexSha256 != evidence.IndexSha256
                || evidence.UncertainCaseIds.Length != 0
            )
                throw new InvalidOperationException("WorkerEvidenceMismatch");
            return outcome;
        }
        finally
        {
            if (!await RemoveOwnedAsync(container, manifest.Binding.RunId, CancellationToken.None))
                throw new InvalidOperationException("OwnedEnvelopeCleanupUnproven");
        }
    }

    internal static async Task<(int Code, string Output)> RunWorkerAsync(
        IContainer container,
        string mode,
        Func<TripsBriefWorkerAdmission> admission,
        Func<string?>? resolveKey,
        CancellationToken ct
    )
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (
            var a in new[]
            {
                "exec",
                "-i",
                container.Id,
                "dotnet",
                "/app/Travel.Trips.Evaluation.dll",
                mode,
            }
        )
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        try
        {
            var errors = ReadBoundedAsync(p.StandardError, 65536, ct);
            await SendAdmissionAsync(p.StandardOutput, p.StandardInput, admission, resolveKey, ct);
            p.StandardInput.Close();
            var output = await ReadBoundedAsync(p.StandardOutput, 65536, ct);
            await p.WaitForExitAsync(ct);
            var errorText = await errors;
            if (mode != "__worker-execute" && p.ExitCode != 0 && output.Length == 0)
                output = "SourceProbeProcessFailure:" + p.ExitCode + ":" + errorText;
            return (p.ExitCode, output);
        }
        finally
        {
            if (!p.HasExited)
                p.Kill(true);
        }
    }

    internal static async Task SendAdmissionAsync(
        TextReader input,
        TextWriter output,
        Func<TripsBriefWorkerAdmission> admission,
        Func<string?>? resolveKey,
        CancellationToken ct
    )
    {
        var ready = await input.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(30), ct);
        if (ready != "WORKER_READY")
            throw new InvalidOperationException("EnvelopeWorkerNotReady");
        await output.WriteLineAsync(JsonSerializer.Serialize(admission()));
        await output.FlushAsync(ct);
        if (resolveKey is not null)
        {
            var acknowledged = await input
                .ReadLineAsync(ct)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(30), ct);
            if (acknowledged != "WORKER_AUTHORIZED")
                throw new InvalidOperationException("WorkerAuthorityNotAcknowledged");
            await output.WriteLineAsync(resolveKey());
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximum,
        CancellationToken ct
    )
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int n;
        while ((n = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (result.Length + n > maximum)
                throw new InvalidOperationException("WorkerOutputTooLarge");
            result.Append(buffer, 0, n);
        }
        return result.ToString();
    }

    internal static async Task VerifyHostCapsAsync(
        IContainer container,
        string archive,
        Guid runId,
        string image,
        string app,
        CancellationToken ct
    )
    {
        var inspect = await ProcessAsync(
            "docker",
            Directory.GetCurrentDirectory(),
            ["inspect", "--format", "{{json .HostConfig}}", container.Id],
            ct
        );
        using var d = JsonDocument.Parse(inspect.Output);
        var h = d.RootElement;
        if (
            h.GetProperty("Memory").GetInt64()
                != TripsBriefEvaluationResourcePolicy.MaximumMemoryBytes
            || h.GetProperty("MemorySwap").GetInt64()
                != TripsBriefEvaluationResourcePolicy.MaximumMemoryBytes
            || h.GetProperty("NanoCpus").GetInt64() != TripsBriefEvaluationResourcePolicy.NanoCpus
            || !h.GetProperty("ReadonlyRootfs").GetBoolean()
            || h.GetProperty("LogConfig").GetProperty("Type").GetString() != "none"
            || h.GetProperty("PortBindings").EnumerateObject().Any()
        )
            throw new InvalidOperationException("EnvelopeHostCapsUnproven");
        var mounts = h.GetProperty("Mounts").EnumerateArray().ToArray();
        if (
            mounts.Length != 2
            || !mounts.Any(v =>
                v.GetProperty("Target").GetString() == "/app"
                && (v.TryGetProperty("ReadOnly", out var readOnly) && readOnly.GetBoolean())
            )
            || !mounts.Any(v =>
                v.GetProperty("Target").GetString()
                    == TripsBriefEvaluationResourcePolicy.ArchiveMount
                && !(v.TryGetProperty("ReadOnly", out var readOnly) && readOnly.GetBoolean())
            )
        )
            throw new InvalidOperationException("EnvelopeArchiveMountChanged");
        if (
            !mounts.Any(v =>
                v.GetProperty("Target").GetString() == "/app"
                && SameMount(v.GetProperty("Source").GetString(), app)
            )
            || !mounts.Any(v =>
                v.GetProperty("Target").GetString()
                    == TripsBriefEvaluationResourcePolicy.ArchiveMount
                && SameMount(v.GetProperty("Source").GetString(), archive)
            )
        )
            throw new InvalidOperationException("EnvelopeMountSourceChanged");
        if (
            h.GetProperty("CapDrop").EnumerateArray().Single().GetString() != "ALL"
            || !h.GetProperty("SecurityOpt")
                .EnumerateArray()
                .Any(v => v.GetString() == "no-new-privileges:true")
        )
            throw new InvalidOperationException("EnvelopePrivilegesChanged");
        var limits = h.GetProperty("Ulimits").EnumerateArray().ToArray();
        if (
            limits.Length != 1
            || limits[0].GetProperty("Name").GetString() != "fsize"
            || limits[0].GetProperty("Soft").GetInt64()
                != TripsBriefEvaluationResourcePolicy.ArchiveBytes
            || limits[0].GetProperty("Hard").GetInt64()
                != TripsBriefEvaluationResourcePolicy.ArchiveBytes
        )
            throw new InvalidOperationException("EnvelopeFileLimitChanged");
        var actualImage = (
            await ProcessAsync(
                "docker",
                Directory.GetCurrentDirectory(),
                ["inspect", "--format", "{{.Image}}", container.Id],
                ct
            )
        ).Output.Trim();
        if (actualImage != image)
            throw new InvalidOperationException("EnvelopeImageChanged");
        var owner = (
            await ProcessAsync(
                "docker",
                Directory.GetCurrentDirectory(),
                [
                    "inspect",
                    "--format",
                    "{{index .Config.Labels \"travel.trips.envelope\"}}",
                    container.Id,
                ],
                ct
            )
        ).Output.Trim();
        if (owner != runId.ToString("N"))
            throw new InvalidOperationException("EnvelopeOwnerChanged");
    }

    private static bool SameMount(string? source, string expected)
    {
        string Normalize(string value)
        {
            value = value.Replace('\\', '/').TrimEnd('/');
            const string prefix = "/run/desktop/mnt/host/";
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                value = value[prefix.Length] + ":" + value[(prefix.Length + 1)..];
            return value;
        }
        return source is not null
            && string.Equals(
                Normalize(source),
                Normalize(expected),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            );
    }

    private static async Task<bool> RemoveOwnedAsync(
        IContainer container,
        Guid runId,
        CancellationToken ct
    )
    {
        string id;
        try
        {
            id = container.Id;
        }
        catch (InvalidOperationException)
        {
            var found = (
                await ProcessAsync(
                    "docker",
                    Directory.GetCurrentDirectory(),
                    ["ps", "-aq", "--filter", "label=travel.trips.envelope=" + runId.ToString("N")],
                    CancellationToken.None
                )
            ).Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (found.Length == 0)
                return true;
            if (found.Length != 1)
                return false;
            id = found[0];
        }
        return await RemoveOwnedIdAsync(
            id,
            runId,
            args =>
                ProcessAsync(
                    "docker",
                    Directory.GetCurrentDirectory(),
                    args,
                    CancellationToken.None,
                    allowFailure: true
                ),
            () => container.DisposeAsync()
        );
    }

    internal static async Task<bool> RemoveOwnedIdAsync(
        string id,
        Guid runId,
        Func<string[], Task<(int Code, string Output)>> run,
        Func<ValueTask> dispose
    )
    {
        async Task<int> Presence()
        {
            var list = await run([
                "container",
                "ls",
                "--all",
                "--quiet",
                "--no-trunc",
                "--filter",
                "id=" + id,
            ]);
            if (list.Code != 0)
                return -1;
            return string.IsNullOrWhiteSpace(list.Output) ? 0 : 1;
        }
        var owner = await run([
            "inspect",
            "--format",
            "{{index .Config.Labels \"travel.trips.envelope\"}}",
            id,
        ]);
        if (owner.Code != 0)
            return await Presence() == 0;
        if (owner.Output.Trim() != runId.ToString("N"))
            return false;
        await dispose();
        var presence = await Presence();
        if (presence <= 0)
            return presence == 0;
        if ((await run(["rm", "--force", "--volumes", id])).Code != 0)
            return false;
        return await Presence() == 0;
    }

    internal static async Task<(int Code, string Output)> ProcessAsync(
        string executable,
        string root,
        string[] args,
        CancellationToken ct,
        bool allowFailure = false,
        int timeoutSeconds = 30
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (var a in args)
            start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!p.HasExited)
                p.Kill(true);
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        if (p.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException(
                "OwnedPreparationFailed:"
                    + executable
                    + ":"
                    + p.ExitCode
                    + ":"
                    + (error.Length <= 4000 ? error : error[^4000..])
            );
        return (p.ExitCode, output);
    }

    private static string Physical(string path)
    {
        for (
            var directory = new DirectoryInfo(path);
            directory is not null;
            directory = directory.Parent
        )
            if (directory.LinkTarget is not null)
                return Path.Combine(
                    directory.ResolveLinkTarget(true)!.FullName,
                    Path.GetRelativePath(directory.FullName, path)
                );
        return path;
    }

    private static string Hash(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
}
