using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;

namespace Travel.Tests.Contract.Trips;

internal static class TripsBriefEvaluationTestFixture
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);
    internal static string Packet =>
        Path.Combine(AppContext.BaseDirectory, "TripsBriefExtraction", "qualification-cases.json");

    internal static TripsBriefEvaluationBinding Binding() =>
        new(
            Guid.NewGuid(),
            "fictional-mechanics-only",
            true,
            TripsBriefEvaluationPacket.HashFile(Packet),
            new string('a', 64),
            TripsBriefEvaluationRunner.EmbeddedPromptSha256(),
            "fictional-model",
            1m,
            5m,
            1m,
            48,
            RunReceiptDirectory: Path.Combine(
                Path.GetTempPath(),
                "mechanical-evidence",
                Guid.NewGuid().ToString("N")
            )
        );

    internal static TripsBriefEvaluationManifest Manifest()
    {
        var b = Binding();
        var a = new TripsBriefEvaluationArtifacts(
            new string('a', 40),
            b.SourceTreeSha256,
            b.PacketSha256,
            b.PromptSha256,
            TripsBriefExtractor.SchemaSha256(),
            new string('a', 64),
            new string('a', 64),
            new string('a', 64),
            new string('a', 64),
            true,
            TripsBriefWireProjection.Sha256(),
            TripsBriefEvaluationResourcePolicy.Sha256(),
            new string('a', 64),
            "sha256:" + new string('a', 64)
        );
        var o = new TripsBriefEvaluationOptions(
            "anthropic",
            b.Model,
            b.Model,
            1m,
            5m,
            .03m,
            .30m,
            TripsBriefPolicyIdentity.Compute(
                "anthropic",
                b.Model,
                TripsBriefMessageIdentity.PrivacyNoticeVersion
            )
        );
        return new(1, b, a, o, "https://api.anthropic.com", true, Now.AddHours(7));
    }

    internal sealed class Clock : TimeProvider
    {
        private long ticks;
        private DateTimeOffset utc = Now;

        public override DateTimeOffset GetUtcNow() => utc;

        public override long GetTimestamp() => ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        internal void Advance(TimeSpan d)
        {
            ticks += d.Ticks;
            utc += d;
        }

        internal void SetUtc(DateTimeOffset d) => utc = d;
    }
}
