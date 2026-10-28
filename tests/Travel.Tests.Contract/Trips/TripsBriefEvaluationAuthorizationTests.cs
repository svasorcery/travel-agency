using System.Text.Json;
using Shouldly;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationAuthorizationTests
{
    [Fact]
    public void Postgres_limits_do_not_prove_the_combined_worker_ledger_budget()
    {
        var m = TripsBriefEvaluationTestFixture.Manifest();
        var check = TripsBriefEvaluationAuthorization.Check(
            m,
            m.ExpectedArtifacts,
            TripsBriefEvaluationTestFixture.Now
        );
        check.ReadyForExecution.ShouldBeFalse();
        check.CanConstructExecutionResources.ShouldBeTrue();
    }

    [Fact]
    public void Authority_clock_includes_inspection_pause_and_ignores_utc_rollback()
    {
        var clock = new TripsBriefEvaluationTestFixture.Clock();
        var authority = new TripsBriefEvaluationAuthorizationClock(clock);
        clock.Advance(TimeSpan.FromMinutes(20));
        clock.SetUtc(TripsBriefEvaluationTestFixture.Now.AddHours(-1));
        authority
            .Remaining(TripsBriefEvaluationTestFixture.Now.AddMinutes(10))
            .ShouldBe(TimeSpan.FromMinutes(-10));
    }

    [Fact]
    public void Current_sdk_projection_is_not_execution_authority()
    {
        var m = TripsBriefEvaluationTestFixture.Manifest();
        var check = TripsBriefEvaluationAuthorization.Check(
            m,
            m.ExpectedArtifacts,
            TripsBriefEvaluationTestFixture.Now
        );
        check.CanConstructExecutionResources.ShouldBeTrue();
        check.ReadyForExecution.ShouldBeFalse();
    }

    [Fact]
    public void Missing_approval_and_pending_selection_are_not_executable()
    {
        var m = TripsBriefEvaluationTestFixture.Manifest();
        TripsBriefEvaluationAuthorization
            .Check(m, m.ExpectedArtifacts, TripsBriefEvaluationTestFixture.Now)
            .Reasons.ShouldBeEmpty();
        foreach (
            var bad in new[]
            {
                m with
                {
                    Binding = m.Binding with { Authorized = false },
                },
                m with
                {
                    Binding = m.Binding with { Model = "" },
                },
                m with
                {
                    AuthorizationExpiresAtUtc = TripsBriefEvaluationTestFixture.Now,
                },
                m with
                {
                    FictionalOnly = false,
                },
                m with
                {
                    ProtocolVersion = 2,
                },
            }
        )
            TripsBriefEvaluationAuthorization
                .Check(bad, m.ExpectedArtifacts, TripsBriefEvaluationTestFixture.Now)
                .Reasons.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Unknown_duplicate_or_changed_binding_fails_before_resources()
    {
        var m = TripsBriefEvaluationTestFixture.Manifest();
        var json = JsonSerializer.Serialize(m);
        Should.Throw<JsonException>(() =>
            TripsBriefEvaluationAuthorization.Parse(
                json.Replace("\"ProtocolVersion\":1", "\"ProtocolVersion\":1,\"Unknown\":1")
            )
        );
        Should.Throw<JsonException>(() =>
            TripsBriefEvaluationAuthorization.Parse(
                json.Replace("\"ProtocolVersion\":1", "\"ProtocolVersion\":1,\"ProtocolVersion\":1")
            )
        );
        foreach (
            var property in typeof(TripsBriefEvaluationArtifacts)
                .GetProperties()
                .Where(x => x.PropertyType == typeof(string))
        )
        {
            var changed = JsonSerializer.SerializeToNode(m.ExpectedArtifacts)!;
            changed[property.Name] = new string('b', property.Name == "Commit" ? 40 : 64);
            var observed = changed.Deserialize<TripsBriefEvaluationArtifacts>()!;
            TripsBriefEvaluationAuthorization
                .Check(m, observed, TripsBriefEvaluationTestFixture.Now)
                .Reasons.ShouldContain("ArtifactsDirtyOrChanged");
        }
        TripsBriefEvaluationAuthorization
            .Check(
                m,
                m.ExpectedArtifacts with
                {
                    Clean = false,
                },
                TripsBriefEvaluationTestFixture.Now
            )
            .Reasons.ShouldContain("ArtifactsDirtyOrChanged");
    }

    [Fact]
    public void Prices_limits_policy_and_binary_must_match()
    {
        var m = TripsBriefEvaluationTestFixture.Manifest();
        foreach (
            var o in new[]
            {
                m.Options with
                {
                    InputUsdPerMillion = 2m,
                },
                m.Options with
                {
                    MaxHourlyCostUsd = .29m,
                },
                m.Options with
                {
                    MaxRequestCostUsd = .031m,
                },
                m.Options with
                {
                    PricingModel = "other",
                },
                m.Options with
                {
                    Policy = new string('b', 64),
                },
                m.Options with
                {
                    ProcessorId = "other",
                },
            }
        )
            TripsBriefEvaluationAuthorization
                .Check(
                    m with
                    {
                        Options = o,
                    },
                    m.ExpectedArtifacts,
                    TripsBriefEvaluationTestFixture.Now
                )
                .Reasons.Length.ShouldBeGreaterThan(0);
    }
}
