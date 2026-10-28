using System.Text.Json;
using Microsoft.Extensions.AI;
using Travel.AI.TripsBriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;

namespace Travel.Trips.Evaluation;

public sealed record TripsBriefEnvelopeOutcome(
    Guid RunId,
    string? IndexSha256,
    bool Passed,
    string? StopReason,
    int PipelineAttempts,
    int ProviderCalls,
    decimal? SpendUsd,
    TimeSpan Duration,
    string CompletionState,
    string EvidenceKind
);

public interface ITripsBriefEvaluationEnvironment : IAsyncDisposable
{
    TimeProvider Clock { get; }
    TripsBriefEvaluationArtifacts InspectArtifacts(string repoRoot);
    Task<TripsBriefEnvelopeOutcome> RunOwnedAsync(
        TripsBriefEvaluationManifest manifest,
        string repoRoot,
        string manifestSha256,
        TripsBriefEvaluationAuthorizationClock authority,
        CancellationToken ct
    );
}

public static class TripsBriefEvaluationCommand
{
    public const string PacketRelativePath =
        "tests/Travel.Tests.AiEvals/TripsBriefExtraction/qualification-cases.json";

    public static async Task<int> RunAsync(
        string[] args,
        ITripsBriefEvaluationEnvironment environment,
        TextWriter output,
        CancellationToken ct
    )
    {
        var clock = environment.Clock;
        var authority = new TripsBriefEvaluationAuthorizationClock(clock);
        bool mayHaveDispatched = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var mode = args.Length == 0 ? "preflight" : args[0];
            if (mode is not ("preflight" or "execute"))
                return await Status(2, "ArgumentsRejected");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i += 2)
                if (
                    i + 1 >= args.Length
                    || args[i] is not ("--repo-root" or "--manifest" or "--confirm-manifest-sha256")
                    || !values.TryAdd(args[i], args[i + 1])
                )
                    return await Status(2, "ArgumentsRejected");
            var root = Path.GetFullPath(
                values.GetValueOrDefault("--repo-root", Directory.GetCurrentDirectory())
            );
            var observed = environment.InspectArtifacts(root);
            if (!values.TryGetValue("--manifest", out var path))
                return await Status(
                    mode == "preflight" ? 0 : 2,
                    "SelectionAndAuthorizationPending",
                    observed
                );
            if (
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || new FileInfo(path).Length > 65536
            )
                return await Status(2, "ManifestRejected");
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var hash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(bytes)
            );
            var manifest = TripsBriefEvaluationAuthorization.Parse(
                System.Text.Encoding.UTF8.GetString(bytes)
            );
            var check = TripsBriefEvaluationAuthorization.Check(
                manifest,
                observed,
                authority.EffectiveUtcNow
            );
            if (mode == "preflight")
            {
                await output.WriteLineAsync(
                    JsonSerializer.Serialize(
                        new
                        {
                            check.ReadyForExecution,
                            ProviderCalls = 0,
                            check.Reasons,
                            ObservedArtifacts = observed,
                        }
                    )
                );
                return 0;
            }
            if (
                !check.CanConstructExecutionResources
                || !values.TryGetValue("--confirm-manifest-sha256", out var confirm)
                || confirm != hash
            )
                return await Status(2, "ExplicitBoundAuthorizationRequired");
            TripsBriefEvaluationFileJournal.CheckRoot(manifest.Binding.RunReceiptDirectory!);
            var packet = Path.Combine(root, PacketRelativePath);
            TripsBriefEvaluationPacket.Load(packet);
            if (authority.Remaining(manifest.AuthorizationExpiresAtUtc) <= TimeSpan.FromSeconds(5))
                return await Status(2, "AuthorizationExpiredBeforeFactories");
            mayHaveDispatched = true;
            var receipt = await environment.RunOwnedAsync(manifest, root, hash, authority, ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(receipt));
            return receipt.Passed ? 0
                : ct.IsCancellationRequested ? 130
                : 3;
        }
        catch (OperationCanceledException)
        {
            return await Status(130, "StoppedNoPass");
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return await Status(2, "NotReadyNoPass");
        }

        async Task<int> Status(
            int code,
            string reason,
            TripsBriefEvaluationArtifacts? artifacts = null
        )
        {
            await output.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        ReadyForExecution = false,
                        ProviderCalls = mayHaveDispatched ? (int?)null : 0,
                        Reason = reason,
                        ObservedArtifacts = artifacts,
                    }
                )
            );
            return code;
        }
    }
}
