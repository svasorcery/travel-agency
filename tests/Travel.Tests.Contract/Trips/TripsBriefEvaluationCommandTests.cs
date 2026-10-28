using Microsoft.Extensions.AI;
using Shouldly;
using Travel.AI.TripsBriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Travel.Trips.Evaluation;
using Xunit;
using static Travel.Tests.Contract.Trips.TripsBriefEvaluationTestFixture;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationCommandTests
{
    [Fact]
    public async Task Invalid_case_scope_is_rejected_before_owned_factories()
    {
        foreach (
            var ids in new[]
            {
                Array.Empty<string>(),
                new[] { "UNKNOWN" },
                new[] { "D01", "D01" },
                new[] { "E01" },
            }
        )
        {
            var m = Manifest();
            m = m with { Binding = m.Binding with { ApprovedCaseIds = ids } };
            await using var env = new Environment { Override = m };
            using var output = new StringWriter();
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            await File.WriteAllTextAsync(
                path,
                System.Text.Json.JsonSerializer.Serialize(m),
                TestContext.Current.CancellationToken
            );
            try
            {
                (
                    await TripsBriefEvaluationCommand.RunAsync(
                        [
                            "execute",
                            "--manifest",
                            path,
                            "--confirm-manifest-sha256",
                            TripsBriefEvaluationPacket.HashFile(path),
                            "--repo-root",
                            Root(),
                        ],
                        env,
                        output,
                        TestContext.Current.CancellationToken
                    )
                ).ShouldBe(2);
                env.Factories.ShouldBe(0);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Default_preflight_never_resolves_secrets_or_resources()
    {
        await using var env = new Environment();
        using var output = new StringWriter();
        (
            await TripsBriefEvaluationCommand.RunAsync(
                [],
                env,
                output,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(0);
        output.ToString().ShouldContain("\"ReadyForExecution\":false");
        output.ToString().ShouldContain("\"ProviderCalls\":0");
        env.Factories.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_requires_explicit_bound_approval_before_factories()
    {
        await using var env = new Environment();
        using var output = new StringWriter();
        (
            await TripsBriefEvaluationCommand.RunAsync(
                ["execute"],
                env,
                output,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(2);
        env.Factories.ShouldBe(0);
        var path = Path.Combine(
            Path.GetTempPath(),
            "mechanical-manifest-" + Guid.NewGuid() + ".json"
        );
        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(Manifest()),
            TestContext.Current.CancellationToken
        );
        try
        {
            (
                await TripsBriefEvaluationCommand.RunAsync(
                    [
                        "execute",
                        "--manifest",
                        path,
                        "--confirm-manifest-sha256",
                        new string('0', 64),
                    ],
                    env,
                    output,
                    TestContext.Current.CancellationToken
                )
            ).ShouldBe(2);
            env.Factories.ShouldBe(0);
            (
                await TripsBriefEvaluationCommand.RunAsync(
                    [
                        "execute",
                        "--manifest",
                        path,
                        "--confirm-manifest-sha256",
                        TripsBriefEvaluationPacket.HashFile(path),
                        "--repo-root",
                        Root(),
                    ],
                    env,
                    output,
                    TestContext.Current.CancellationToken
                )
            ).ShouldBe(2);
            env.Factories.ShouldBe(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Cancellation_does_not_publish_passed()
    {
        await using var env = new Environment();
        using var output = new StringWriter();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        (await TripsBriefEvaluationCommand.RunAsync(["execute"], env, output, stop.Token)).ShouldBe(
            130
        );
        env.Factories.ShouldBe(0);
        output.ToString().ShouldNotContain("\"Passed\":true");
    }

    [Theory]
    [InlineData("--model")]
    [InlineData("--provider")]
    [InlineData("--input")]
    [InlineData("--fake-clock")]
    public async Task No_implicit_model_price_or_provider(string option)
    {
        await using var env = new Environment();
        using var output = new StringWriter();
        (
            await TripsBriefEvaluationCommand.RunAsync(
                ["preflight", option, "anything"],
                env,
                output,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(2);
        env.Factories.ShouldBe(0);
    }

    private sealed class Environment : ITripsBriefEvaluationEnvironment
    {
        public int Factories;
        public TripsBriefEvaluationManifest? Override;
        public TimeProvider Clock { get; } = new Clock();

        public TripsBriefEvaluationArtifacts InspectArtifacts(string root) =>
            (Override ?? Manifest()).ExpectedArtifacts;

        public Task<TripsBriefEnvelopeOutcome> RunOwnedAsync(
            TripsBriefEvaluationManifest m,
            string root,
            string hash,
            TripsBriefEvaluationAuthorizationClock authority,
            CancellationToken ct
        )
        {
            Factories++;
            throw new InvalidOperationException("FakeFactoryOnly");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}
