using Shouldly;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;
using static Travel.Tests.Contract.Trips.TripsBriefEvaluationTestFixture;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationArchiveTests
{
    [Fact]
    public void Changed_header_cannot_reuse_original_raw_approval_hash()
    {
        var original = Manifest();
        var changed = original with { Binding = original.Binding with { MaxCalls = 1 } };
        var path = TripsBriefEvaluationArchiveJournal.Create(changed, new string('a', 64), true);
        Should.Throw<InvalidOperationException>(() =>
            TripsBriefEvaluationArchiveJournal.Open(
                path,
                changed,
                new string('a', 64),
                true,
                TripsBriefEvaluationArchiveJournal.CanonicalManifestSha256(original)
            )
        );
    }

    [Fact]
    public async Task Subset_binding_survives_independent_manifest_round_trips()
    {
        var original = Manifest();
        var m = original with
        {
            Binding = original.Binding with { ApprovedCaseIds = ["D01"], MaxCalls = 1 },
        };
        var path = TripsBriefEvaluationArchiveJournal.Create(m, new string('a', 64), true);
        var copy = System.Text.Json.JsonSerializer.Deserialize<TripsBriefEvaluationManifest>(
            System.Text.Json.JsonSerializer.Serialize(m)
        )!;
        using (
            var journal = TripsBriefEvaluationArchiveJournal.Open(
                path,
                copy,
                new string('a', 64),
                true
            )
        )
            await journal.CompleteAsync(
                new(
                    copy.Binding,
                    [
                        new(
                            "D01",
                            "development",
                            "NotRun",
                            false,
                            false,
                            false,
                            false,
                            0,
                            null,
                            null,
                            null
                        ),
                    ],
                    "StopRequested",
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    TimeSpan.Zero,
                    false
                ),
                TestContext.Current.CancellationToken
            );
        TripsBriefEvaluationArchiveJournal.Read(path).Receipt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Fixed_archive_is_durable_bound_and_never_reuses_authority()
    {
        var m = Manifest();
        var path = TripsBriefEvaluationArchiveJournal.Create(
            m,
            new string('a', 64),
            probeOnly: true
        );
        new FileInfo(path).Length.ShouldBe(64L * 1024 * 1024);
        using (
            var journal = TripsBriefEvaluationArchiveJournal.Open(
                path,
                m,
                new string('a', 64),
                probeOnly: true
            )
        )
        {
            await journal.RecordAttemptAsync(
                new(m.Binding.RunId, "D01", Guid.NewGuid(), new string('a', 64), .01m, Now),
                TestContext.Current.CancellationToken
            );
        }
        var read = TripsBriefEvaluationArchiveJournal.Read(path);
        read.UncertainCaseIds.ShouldBe(["D01"]);
        Should.Throw<IOException>(() =>
            TripsBriefEvaluationArchiveJournal.Create(m, new string('a', 64), probeOnly: true)
        );
        Should.Throw<InvalidOperationException>(() =>
            TripsBriefEvaluationArchiveJournal.Open(path, m, new string('b', 64), probeOnly: true)
        );
        Should.Throw<InvalidOperationException>(() =>
            TripsBriefEvaluationArchiveJournal.Open(path, m, new string('a', 64), probeOnly: false)
        );
    }

    [Fact]
    public async Task Archive_rejects_uncontrolled_status_and_reports_torn_result_without_intent()
    {
        var m = Manifest();
        var path = TripsBriefEvaluationArchiveJournal.Create(m, new string('a', 64), true);
        using (
            var journal = TripsBriefEvaluationArchiveJournal.Open(
                path,
                m,
                new string('a', 64),
                true
            )
        )
            await Should.ThrowAsync<InvalidOperationException>(() =>
                journal.RecordResultAsync(
                    new(
                        "D01",
                        "discovery",
                        "raw exception or prompt",
                        false,
                        false,
                        false,
                        false,
                        0,
                        null,
                        null,
                        null
                    ),
                    TestContext.Current.CancellationToken
                )
            );
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            file.Position = 2 * 128 * 1024 + 64;
            file.WriteByte(42);
            file.Flush(true);
        }
        TripsBriefEvaluationArchiveJournal.Read(path).UncertainCaseIds.ShouldBe(["D01"]);
    }

    [Fact]
    public void Budget_counts_persistent_evidence_and_all_writable_filesystems()
    {
        TripsBriefEvaluationResourcePolicy.TmpfsBytes.Sum().ShouldBe(192L * 1024 * 1024);
        (
            TripsBriefEvaluationResourcePolicy.TmpfsBytes.Sum()
            + TripsBriefEvaluationResourcePolicy.ArchiveBytes
        ).ShouldBe(256L * 1024 * 1024);
        TripsBriefEvaluationResourcePolicy.MaximumMemoryBytes.ShouldBe(2L * 1024 * 1024 * 1024);
    }
}
