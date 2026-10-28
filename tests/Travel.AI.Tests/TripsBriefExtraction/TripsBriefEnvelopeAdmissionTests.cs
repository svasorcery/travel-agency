using Shouldly;
using Travel.Trips.Evaluation;
using Xunit;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefEnvelopeAdmissionTests
{
    [Fact]
    public async Task Key_resolver_waits_for_validated_worker_ack()
    {
        var reads = 0;
        using var input = new StringReader(
            "WORKER_READY\n{\"Reason\":\"ArchiveAuthorityChanged\"}\n"
        );
        using var output = new StringWriter();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            TripsBriefEvaluationEnvelope.SendAdmissionAsync(
                input,
                output,
                () => new("hash", 100, "canonical"),
                () =>
                {
                    reads++;
                    return "fictional-key";
                },
                TestContext.Current.CancellationToken
            )
        );
        reads.ShouldBe(0);
        output.ToString().ShouldNotContain("fictional-key");
        using var good = new StringReader("WORKER_READY\nWORKER_AUTHORIZED\n");
        await TripsBriefEvaluationEnvelope.SendAdmissionAsync(
            good,
            output,
            () => new("hash", 100, "canonical"),
            () =>
            {
                reads++;
                return "fictional-key";
            },
            TestContext.Current.CancellationToken
        );
        reads.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Docker_query_failure_never_proves_cleanup(bool afterDisposal)
    {
        var run = Guid.NewGuid();
        var disposed = false;
        var calls = 0;
        var result = await TripsBriefEvaluationEnvelope.RemoveOwnedIdAsync(
            "owned-id",
            run,
            args =>
                Task.FromResult(++calls == 1 && afterDisposal ? (0, run.ToString("N")) : (1, "")),
            () =>
            {
                disposed = true;
                return ValueTask.CompletedTask;
            }
        );
        result.ShouldBeFalse();
        disposed.ShouldBe(afterDisposal);
    }

    [Fact]
    public async Task Confirmed_absence_can_prove_cleanup()
    {
        (
            await TripsBriefEvaluationEnvelope.RemoveOwnedIdAsync(
                "owned-id",
                Guid.NewGuid(),
                args => Task.FromResult((args[0] == "inspect" ? 1 : 0, "")),
                () => ValueTask.CompletedTask
            )
        ).ShouldBeTrue();
    }
}
