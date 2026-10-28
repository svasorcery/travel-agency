using Shouldly;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;
using static Travel.Tests.Contract.Trips.TripsBriefEvaluationTestFixture;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationJournalTests
{
    [Fact]
    public async Task Same_journal_cannot_start_another_run_after_a_stopped_run()
    {
        var b = Binding();
        var clock = new Clock();
        var pipe = new CountingPipeline();
        var journal = new TripsBriefEvaluationFileJournal(b, new string('a', 64));
        await TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Packet,
            b,
            pipe,
            clock,
            journal,
            new(clock),
            stopRequested: () => true,
            cancellationToken: TestContext.Current.CancellationToken
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            TripsBriefEvaluationRunner.RunAuthorizedAsync(
                Packet,
                b,
                pipe,
                clock,
                journal,
                new(clock),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        pipe.Calls.ShouldBe(0);
    }

    private sealed class CountingPipeline : ITripsBriefEvaluationPipeline
    {
        public int Calls;
        public string ModelId => "fictional-model";

        public Task<Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefParsed> ExtractAsync(
            Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefRequested r,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult(
                new Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefParsed(
                    r.CorrelationId,
                    r.ProcessingPolicyVersion,
                    "ModelUnavailable"
                )
            );
        }
    }

    [Fact]
    public async Task Intent_write_failure_has_zero_pipeline_calls()
    {
        var b = Binding();
        var clock = new Clock();
        var pipe = new ForbiddenPipeline();
        var receipt = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Packet,
            b,
            pipe,
            clock,
            new MechanicalJournal { FailIntent = true },
            new(clock),
            cancellationToken: TestContext.Current.CancellationToken
        );
        receipt.Dispatched.ShouldBe(0);
        receipt.Passed.ShouldBeFalse();
        receipt.StopReason.ShouldBe("JournalUnavailable");
    }

    private sealed class ForbiddenPipeline : ITripsBriefEvaluationPipeline
    {
        public string ModelId => "fictional-model";

        public Task<Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefParsed> ExtractAsync(
            Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefRequested request,
            CancellationToken ct
        ) => throw new Xunit.Sdk.XunitException("Pipeline must not be entered");
    }

    [Fact]
    public async Task Crash_between_intent_and_result_is_uncertain()
    {
        var b = Binding();
        var journal = new TripsBriefEvaluationFileJournal(b, new string('a', 64));
        await journal.RecordAttemptAsync(
            new(b.RunId, "D01", Guid.NewGuid(), new string('a', 64), .01m, Now),
            TestContext.Current.CancellationToken
        );
        var evidence = TripsBriefEvaluationFileJournal.Read(b.RunReceiptDirectory!, b.RunId);
        evidence.UncertainCaseIds.ShouldBe(["D01"]);
        evidence.Receipt.ShouldBeNull();
        await Should.ThrowAsync<IOException>(() =>
            journal.RecordAttemptAsync(
                new(b.RunId, "D01", Guid.NewGuid(), new string('a', 64), .01m, Now),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public void Duplicate_authority_and_outside_or_linked_paths_fail()
    {
        var b = Binding();
        Should.Throw<InvalidOperationException>(() =>
            new TripsBriefEvaluationFileJournal(
                b with
                {
                    RunReceiptDirectory = "../outside",
                },
                new string('a', 64)
            )
        );
        Directory.CreateDirectory(b.RunReceiptDirectory!);
        var link = Path.Combine(b.RunReceiptDirectory!, "link");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(
                "New-Item -ItemType Junction -Path '"
                    + link.Replace("'", "''")
                    + "' -Target '"
                    + b.RunReceiptDirectory!.Replace("'", "''")
                    + "' | Out-Null"
            );
            using var process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit();
            process.ExitCode.ShouldBe(0);
        }
        else
            Directory.CreateSymbolicLink(link, b.RunReceiptDirectory!);
        try
        {
            Should.Throw<InvalidOperationException>(() =>
                new TripsBriefEvaluationFileJournal(
                    b with
                    {
                        RunReceiptDirectory = link,
                    },
                    new string('a', 64)
                )
            );
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task Summary_reconstructs_all48_without_inlining_case_bodies()
    {
        var b = Binding();
        var j = new TripsBriefEvaluationFileJournal(b, new string('a', 64));
        var cases = TripsBriefEvaluationPacket
            .Load(Packet)
            .Cases.Select(c =>
            {
                var normalized =
                    Travel.Modules.Trips.Application.BriefExtraction.TripBriefCandidateValidator.Analyze(
                        TripsBriefEvaluationPacket.Input(c, Now)
                    );
                var issues = Enumerable
                    .Range(0, 16)
                    .Select(
                        _ => new Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefIssue(
                            "unsupportedOrigin",
                            TripsBriefEvaluationPacket.Slots,
                            [new(0, 1), new(1, 2), new(2, 3), new(3, 4)]
                        )
                    )
                    .ToArray();
                return new TripsBriefEvalCaseReceipt(
                    c.Id,
                    c.Partition,
                    "Ready",
                    true,
                    false,
                    false,
                    false,
                    1,
                    1,
                    1,
                    .000006m,
                    normalized.Fields,
                    issues
                );
            })
            .ToArray();
        await j.CompleteAsync(
            new(b, cases, "StopRequested", 0, 0, 0, 0, 0, 0m, TimeSpan.Zero, false),
            TestContext.Current.CancellationToken
        );
        var read = TripsBriefEvaluationFileJournal.Read(b.RunReceiptDirectory!, b.RunId);
        read.IndexSha256.ShouldBe(
            TripsBriefEvaluationPacket.HashFile(
                Path.Combine(b.RunReceiptDirectory!, b.RunId.ToString("N") + "-summary.json")
            )
        );
        j.CompletedIndexSha256.ShouldBe(read.IndexSha256);
        read.Receipt!.Passed.ShouldBeFalse();
        read.Receipt.CompletionState.ShouldBe("FinalizationPending");
        var indexPath = Path.Combine(
            b.RunReceiptDirectory!,
            b.RunId.ToString("N") + "-summary.json"
        );
        var altered = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken)
        )!;
        altered["Aggregate"]!["Passed"] = true;
        await File.WriteAllTextAsync(
            indexPath,
            altered.ToJsonString(),
            TestContext.Current.CancellationToken
        );
        Should.Throw<InvalidOperationException>(() =>
            TripsBriefEvaluationFileJournal.Read(b.RunReceiptDirectory!, b.RunId)
        );
        read.Receipt!.Cases.Select(x => x.Id).ShouldBe(cases.Select(x => x.Id));
        Directory.GetFiles(b.RunReceiptDirectory!, "*.json").Length.ShouldBe(49);
        Directory
            .GetFiles(b.RunReceiptDirectory!, "*.json")
            .All(p => new FileInfo(p).Length <= 65536)
            .ShouldBeTrue();
        Directory
            .GetFiles(b.RunReceiptDirectory!, "*.json")
            .Sum(p => new FileInfo(p).Length)
            .ShouldBeGreaterThan(65536);
    }
}
