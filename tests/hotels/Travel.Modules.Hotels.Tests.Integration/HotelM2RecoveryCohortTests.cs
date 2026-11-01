using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2RecoveryCohortTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static (
        HotelM2LoadSchedule Plan,
        HotelM2RecoveryFact[] Facts,
        Func<int, double, bool> Purchase
    ) Recorded()
    {
        using var file = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "m2-v5-recovery-cohort.json")
            )
        );
        var facts = file.RootElement.GetProperty("facts").Deserialize<HotelM2RecoveryFact[]>(Json)!;
        var purchases = file
            .RootElement.GetProperty("purchases")
            .EnumerateArray()
            .Select(p => p.Clone())
            .ToDictionary(p => p.GetProperty("jobId").GetInt32());
        return (
            new(new("S", new(2026, 11, 6))),
            facts,
            (id, end) =>
            {
                var p = purchases[id];
                double? Number(string name) =>
                    p.GetProperty(name).ValueKind == JsonValueKind.Null
                        ? null
                        : p.GetProperty(name).GetDouble();
                int? Status() =>
                    p.GetProperty("status").ValueKind == JsonValueKind.Null
                        ? null
                        : p.GetProperty("status").GetInt32();
                return HotelsLoadLedger.WindowPurchaseSuccess(
                    p.GetProperty("due").GetDouble(),
                    Number("httpCompleted") ?? double.NaN,
                    Number("authoritativeObservedAt"),
                    Status(),
                    p.GetProperty("responseAccepted").GetBoolean()
                        && p.GetProperty("sourceMatched").GetBoolean(),
                    p.GetProperty("effects").ValueKind == JsonValueKind.Null
                        ? null
                        : p.GetProperty("effects").GetInt32(),
                    end
                );
            }
        );
    }

    [Fact]
    public void Recorded_chain_252_is_carry_in_only_after_its_origin_leaves_the_window()
    {
        var (plan, facts, purchase) = Recorded();
        var result = HotelsM2RecoveryCohort.Evaluate(plan.Jobs, facts, purchase, false);
        result.LegacyGatePassed.ShouldBeFalse();
        result.RecoveredAt.ShouldBe(313);
        foreach (var id in new[] { 2596, 2632, 2642 })
        {
            result
                .Windows.Single(w => w.End == 312)
                .Kinds.SelectMany(k => k.EligibleIds)
                .ShouldContain(id);
            result
                .Windows.Single(w => w.End == 313)
                .Kinds.SelectMany(k => k.CarryInIds)
                .ShouldContain(id);
        }
        foreach (var window in result.Windows)
        foreach (var kind in window.Kinds)
            kind.RawOffered.ShouldBe(kind.EligibleIds.Length + kind.CarryInIds.Length);
        plan.Jobs.Count.ShouldBe(3152);
    }

    [Fact]
    public void Failure_of_a_chain_started_inside_window_still_blocks_recovery()
    {
        var (plan, facts, purchase) = Recorded();
        var changed = facts.Select(f => f.JobId == 2670 ? f with { Useful = false } : f).ToArray();
        HotelsM2RecoveryCohort
            .Evaluate(plan.Jobs, changed, purchase, false)
            .RecoveredAt.ShouldBeNull();
    }

    [Fact]
    public void Confirmation_after_window_end_cannot_be_counted_early()
    {
        var (plan, facts, purchase) = Recorded();
        var result = HotelsM2RecoveryCohort.Evaluate(
            plan.Jobs,
            facts,
            (id, end) => id == 2856 ? end >= 315 && purchase(id, end) : purchase(id, end),
            false
        );
        result.RecoveredAt.ShouldBe(315);
        result.Windows.Single(w => w.End == 314).Passed.ShouldBeFalse();
    }

    [Fact]
    public void Missing_fact_cannot_improve_the_denominator()
    {
        var (plan, facts, purchase) = Recorded();
        Should.Throw<InvalidDataException>(() =>
            HotelsM2RecoveryCohort.Evaluate(
                plan.Jobs,
                facts.Where(f => f.JobId != 2596).ToArray(),
                purchase,
                false
            )
        );
    }

    [Fact]
    public void Graph_must_not_have_missing_future_or_cyclic_prerequisites()
    {
        var (plan, _, _) = Recorded();
        foreach (var replacement in new[] { 99999, 2642 })
        {
            var jobs = plan
                .Jobs.Select(j => j.Id == 2596 ? j with { Prerequisite = replacement } : j)
                .ToArray();
            Should.Throw<InvalidDataException>(() => HotelsM2RecoveryCohort.Origins(jobs));
        }
    }

    [Fact]
    public void Membership_does_not_depend_on_observed_success()
    {
        var (plan, facts, purchase) = Recorded();
        var first = HotelsM2RecoveryCohort.Evaluate(plan.Jobs, facts, purchase, false);
        var other = HotelsM2RecoveryCohort.Evaluate(
            plan.Jobs,
            facts.Select(f => f with { Useful = false }).ToArray(),
            (_, _) => false,
            false
        );
        for (var i = 0; i < first.Windows.Length; i++)
        for (var k = 0; k < 4; k++)
        {
            first.Windows[i].Kinds[k].EligibleIds.ShouldBe(other.Windows[i].Kinds[k].EligibleIds);
            first.Windows[i].Kinds[k].CarryInIds.ShouldBe(other.Windows[i].Kinds[k].CarryInIds);
        }
        other.RecoveredAt.ShouldBeNull();
    }

    [Fact]
    public void Duplicate_mismatched_and_not_yet_offered_steps_do_not_improve_results()
    {
        var (plan, facts, purchase) = Recorded();
        Should.Throw<InvalidDataException>(() =>
            HotelsM2RecoveryCohort.Evaluate(plan.Jobs, [.. facts, facts[0]], purchase, false)
        );
        Should.Throw<InvalidDataException>(() =>
            HotelsM2RecoveryCohort.Evaluate(
                plan.Jobs,
                facts.Select((f, i) => i == 0 ? f with { Due = f.Due + 1 } : f).ToArray(),
                purchase,
                false
            )
        );
        var result = HotelsM2RecoveryCohort.Evaluate(plan.Jobs, facts, purchase, false);
        var ids = result.Windows.Single(w => w.End == 313).Kinds.SelectMany(k => k.EligibleIds);
        ids.ShouldAllBe(id => plan.Jobs[id].Due < 313);
    }

    [Fact]
    public void Search_latency_threshold_remains_one_second()
    {
        var (plan, facts, purchase) = Recorded();
        var slow = facts
            .Select(f => f.JobId == 2670 ? f with { Completed = f.Due + 1.1 } : f)
            .ToArray();
        HotelsM2RecoveryCohort
            .Evaluate(plan.Jobs, slow, purchase, false)
            .RecoveredAt.ShouldBeNull();
    }
}
