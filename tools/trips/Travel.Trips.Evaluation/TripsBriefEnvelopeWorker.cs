using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Travel.AI.Persistence;
using Travel.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;

namespace Travel.Trips.Evaluation;

public static class TripsBriefEnvelopeWorker
{
    public static async Task<int> RunAsync(string mode, CancellationToken ct)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        var authority = new TripsBriefEvaluationAuthorizationClock(clock);
        var stage = "Budget";
        TripsBriefEvaluationSchedule? observedSchedule = null;
        var keyTransferred = false;
        try
        {
            var proof = TripsBriefEnvelopeBudget.Verify(); // Before reading any admission or credential.
            stage = "Authority";
            var path = TripsBriefEvaluationResourcePolicy.ArchiveMount;
            var manifest = TripsBriefEvaluationArchiveJournal.Manifest(path);
            var probe = mode is "__worker-probe" or "__worker-probe-crash";
            if (
                TripsBriefEvaluationArchiveJournal.ProbeOnly(path) != probe
                || mode is not ("__worker-probe" or "__worker-probe-crash" or "__worker-execute")
            )
                throw new InvalidOperationException("WorkerModeRejected");
            Console.WriteLine("WORKER_READY");
            var admissionText = await Console.In.ReadLineAsync(ct);
            if (admissionText is null || admissionText.Length > 4096)
                throw new InvalidOperationException("WorkerAdmissionRejected");
            var admission = JsonSerializer.Deserialize<TripsBriefWorkerAdmission>(admissionText)!;
            TimeSpan Remaining() =>
                TimeSpan.FromTicks(admission.RemainingTicks) - clock.GetElapsedTime(started);
            var remaining = Remaining();
            if (
                remaining <= TimeSpan.FromSeconds(5)
                || remaining > TimeSpan.FromHours(6)
                || authority.Remaining(manifest.AuthorizationExpiresAtUtc) <= TimeSpan.Zero
            )
                throw new InvalidOperationException("WorkerAuthorityExpired");
            using var journal = TripsBriefEvaluationArchiveJournal.Open(
                path,
                manifest,
                admission.ManifestSha256,
                probe,
                admission.CanonicalManifestSha256
            );
            if (mode == "__worker-probe-crash")
            {
                journal.BeginRun(manifest.Binding, clock);
                await journal.RecordAttemptAsync(
                    new(
                        manifest.Binding.RunId,
                        "D01",
                        Guid.NewGuid(),
                        admission.ManifestSha256,
                        .009216m,
                        clock.GetUtcNow()
                    ),
                    ct
                );
                return 86;
            }
            if (!probe)
                VerifyExecution(manifest);
            var quotaVerified = probe && VerifyQuotaRejections();
            stage = "Ledger";
            var options = manifest.Options.ToRuntime();
            var dbOptions = new DbContextOptionsBuilder<AiDbContext>()
                .UseNpgsql(
                    "Host=/var/run/postgresql;Database=fictional_evaluation;Username=fictional_evaluator;Pooling=false",
                    AiDbContextConfiguration.ConfigureNpgsql
                )
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var services = new ServiceCollection()
                .AddScoped(_ => new AiDbContext(dbOptions))
                .BuildServiceProvider();
            await using (var db = new AiDbContext(dbOptions))
                await db.Database.MigrateAsync(ct);
            var ledger = new TripsBriefLedger(
                services.GetRequiredService<IServiceScopeFactory>(),
                clock
            );
            using var fakeHttp = probe ? new HttpClient(new ScriptedHttp()) : null;
            string key;
            if (probe)
                key = "fictional-unused-key";
            else
            {
                VerifyExecution(manifest);
                if (Remaining() <= TimeSpan.FromSeconds(5))
                    throw new InvalidOperationException("WorkerAuthorityExpiredBeforeKey");
                Console.WriteLine("WORKER_AUTHORIZED");
                keyTransferred = true;
                var secret = await Console.In.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(secret) || secret.Length > 512)
                    throw new InvalidOperationException("ExplicitWorkerKeyMissing");
                key = secret;
            }
            var schedule = new TripsBriefEvaluationSchedule(clock, Remaining());
            observedSchedule = schedule;
            using var chat = new TripsBriefEvaluationObservedChatClient(
                TripsBriefChatClient.CreateProjectedClient(key, fakeHttp),
                schedule
            );
            var pipeline = new ProductionTripsBriefEvaluationPipeline(
                new(chat, options, new(options, clock), clock),
                ledger,
                options
            );
            stage = "Extraction";
            var receipt = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
                "/app/corpus.json",
                manifest.Binding,
                pipeline,
                clock,
                journal,
                schedule,
                cancellationToken: ct
            );
            if (probe)
            {
                await using var db = new AiDbContext(dbOptions);
                var rows = await db.CostLedger.CountAsync(ct);
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new TripsBriefEnvelopeProbeReceipt(
                            proof.Verified,
                            proof.SharedCgroup,
                            proof.WritableBytes,
                            TripsBriefEvaluationResourcePolicy.ArchiveBytes,
                            path,
                            receipt.JournalIndexSha256,
                            rows,
                            0,
                            receipt.ActualProviderStarts,
                            false,
                            quotaVerified,
                            true
                        )
                    )
                );
                return rows == 1 && receipt.ActualProviderStarts == 1 ? 0 : 3;
            }
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new TripsBriefEnvelopeOutcome(
                        receipt.Binding.RunId,
                        receipt.JournalIndexSha256,
                        receipt.Passed,
                        receipt.StopReason,
                        receipt.Dispatched,
                        receipt.ActualProviderStarts,
                        receipt.ActualSpendUsd,
                        receipt.Duration,
                        receipt.CompletionState,
                        receipt.EvidenceKind
                    )
                )
            );
            return receipt.Passed ? 0 : 3;
        }
        catch (Exception e)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        ReadyForExecution = false,
                        Reason = stage == "Budget" && e is InvalidOperationException
                            ? e.Message
                            : "WorkerNotReady:" + stage,
                        FailureType = e.GetType().Name,
                        RealProviderCalls = keyTransferred
                            ? (int?)observedSchedule?.ProviderStarts
                            : 0,
                    }
                )
            );
            return 2;
        }
    }

    private static bool VerifyQuotaRejections()
    {
        // Run truncate in a child: SIGXFSZ must not kill the worker or corrupt its journal.
        var start = new System.Diagnostics.ProcessStartInfo("truncate")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add((TripsBriefEvaluationResourcePolicy.ArchiveBytes + 1).ToString());
        start.ArgumentList.Add(TripsBriefEvaluationResourcePolicy.ArchiveMount);
        using (var child = System.Diagnostics.Process.Start(start)!)
        {
            if (
                !child.WaitForExit(5000)
                || child.ExitCode == 0
                || new FileInfo(TripsBriefEvaluationResourcePolicy.ArchiveMount).Length
                    != TripsBriefEvaluationResourcePolicy.ArchiveBytes
            )
                throw new InvalidOperationException("ArchiveKernelLimitNotEnforced");
        }
        const string file = "/evaluation/quota-probe";
        var rejected = false;
        try
        {
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 33; i++)
                stream.Write(block);
            stream.Flush(true);
        }
        catch (IOException)
        {
            rejected = true;
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        if (!rejected)
            throw new InvalidOperationException("TmpfsKernelLimitNotEnforced");
        return true;
    }

    private static void VerifyExecution(TripsBriefEvaluationManifest manifest)
    {
        var source = JsonSerializer.Deserialize<TripsBriefEvaluationArtifacts>(
            File.ReadAllText("/app/source-identity.json")
        )!;
        if (
            !source.Clean
            || source.Commit != manifest.ExpectedArtifacts.Commit
            || source.SourceTree != manifest.ExpectedArtifacts.SourceTree
            || TripsBriefEvaluationPacket.HashFile("/app/corpus.json")
                != manifest.Binding.PacketSha256
            || manifest.Binding.PromptSha256 != TripsBriefEvaluationRunner.EmbeddedPromptSha256()
            || manifest.ExpectedArtifacts.Schema != TripsBriefExtractor.SchemaSha256()
            || manifest.ExpectedArtifacts.WireSchema != TripsBriefWireProjection.Sha256()
            || manifest.ExpectedArtifacts.ResourcePolicy
                != TripsBriefEvaluationResourcePolicy.Sha256()
            || manifest.ExpectedArtifacts.WorkerClosure
                != TripsBriefEvaluationEnvelope.Closure("/app")
            || source.Normalizer != manifest.ExpectedArtifacts.Normalizer
            || source.Lexicon != manifest.ExpectedArtifacts.Lexicon
            || source.Packages != manifest.ExpectedArtifacts.Packages
        )
            throw new InvalidOperationException("WorkerSourceBindingChanged");
        // Explicit operator artifact/price/processor authority is still required. Source tests only use probe mode.
        if (
            !TripsBriefEvaluationAuthorization
                .CheckBinding(manifest, manifest.ExpectedArtifacts, TimeProvider.System.GetUtcNow())
                .CanConstructExecutionResources
        )
            throw new InvalidOperationException("WorkerExecutionNotAuthorized");
    }

    private sealed class ScriptedHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r,
            CancellationToken ct
        )
        {
            var c = TripsBriefEvaluationPacket.Load("/app/corpus.json").Cases[0];
            var candidate = TripBriefCandidateValidator.Analyze(
                TripsBriefEvaluationPacket.Input(c, TimeProvider.System.GetUtcNow())
            );
            var json = JsonSerializer.Serialize(
                new { fields = candidate.Fields, issues = candidate.Issues },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );
            var response = new
            {
                id = "fictional",
                type = "message",
                role = "assistant",
                model = "fictional-model",
                content = new[] { new { type = "text", text = json } },
                stop_reason = "end_turn",
                usage = new { input_tokens = 10, output_tokens = 5 },
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(response),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
