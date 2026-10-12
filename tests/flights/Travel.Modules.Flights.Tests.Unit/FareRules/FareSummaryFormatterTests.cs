using System.Text.Json;
using Shouldly;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Core.ValueObjects;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class FareSummaryFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_includes_root_and_each_leg_without_supplier_labels()
    {
        var facts = FareSummaryFormatter.BuildFacts(Snapshot());
        facts.Length.ShouldBe(3);
        facts.Count(x => x.Scopes.Single().Kind == "leg").ShouldBe(1);
        JsonSerializer.Serialize(facts).ShouldNotContain("Secret supplier brand");
        JsonSerializer.Serialize(facts).ShouldNotContain("Secret fare basis");
    }

    [Fact]
    public void Controlled_text_keeps_exact_fee_root_leg_and_mandatory_notices()
    {
        var root = FareCondition
            .Create(FarePermission.NotAllowed, null, "Known", "NotProvided")
            .Value;
        var leg = FareCondition
            .Create(
                FarePermission.Allowed,
                Money
                    .Create(0.0000000000000000000000000001m, CurrencyCode.Create("GBP").Value)
                    .Value,
                "Known",
                "Known"
            )
            .Value;
        var summary = FareSummaryFormatter.Current(
            Snapshot(root, leg),
            Now,
            hasSelectedServices: true
        );
        summary.State.ShouldBe("Current");
        summary.Presentation.ShouldBe("Deterministic");
        var change = summary.Sections.Single(x => x.Kind == "change").Lines;
        change.Length.ShouldBe(2);
        change[0].Scope.ShouldContain("Всё предложение");
        change[0].Text.ShouldContain("не разрешён");
        change[1].Scope.ShouldContain("Участки 1");
        change[1].Text.ShouldContain("GBP");
        change[1].Text.ShouldContain("0.0000000000000000000000000001");
        summary
            .Legs[0]
            .ChangeBeforeDeparture.Penalty!.Amount.ShouldBe(0.0000000000000000000000000001m);
        summary.MissingInformation.Length.ShouldBeGreaterThanOrEqualTo(4);
        summary.Notices.ShouldContain(x => x.Contains("дополнительного багажа"));
        summary.Notices.ShouldContain(x => x.Contains("разницы тарифа"));
        summary.Notices.ShouldContain(x => x.Contains("в банк"));
    }

    [Fact]
    public void Expiry_and_history_never_become_current_and_invalid_snapshot_is_unavailable()
    {
        var snapshot = Snapshot();
        FareSummaryFormatter.Current(snapshot, Now.AddHours(1)).State.ShouldBe("Stale");
        FareSummaryFormatter.Current(snapshot, Now.AddTicks(-1)).State.ShouldBe("Unavailable");
        var history = FareSummaryFormatter.Historical(snapshot, Now);
        history.Provenance.ShouldBe("Historical");
        history.State.ShouldBe("Stale");
        history.Presentation.ShouldBe("Deterministic");
        history.Notices.ShouldContain(x => x.Contains("при расчёте"));
        var invalid = JsonSerializer.Deserialize<QuotedFareSnapshot>(
            JsonSerializer.Serialize(snapshot).Replace(new string('a', 64), "bad")
        )!;
        FareSummaryFormatter.Current(invalid, Now).State.ShouldBe("Unavailable");
        var missing = FareSummaryFormatter.Unavailable(
            snapshot.AggregateId,
            Now,
            offerId: snapshot.OfferId,
            quoteRevision: snapshot.QuoteRevision,
            segmentCounts: [1]
        );
        missing.OfferId.ShouldBe(snapshot.OfferId);
        missing.SourceObservedAt.ShouldBeNull();
    }

    [Fact]
    public void Valid_plan_cannot_remove_notices_and_omitted_duplicate_or_root_leg_merge_falls_back()
    {
        var snapshot = Snapshot();
        var facts = FareSummaryFormatter.BuildFacts(snapshot);
        var plan = facts
            .GroupBy(x => x.Section)
            .Select(s => new FareSummaryPlanSection(
                s.Key,
                s.Select(f => new FareSummaryPlanGroup([f.Id])).ToArray()
            ))
            .ToArray();
        FareSummaryPlanValidator.ValidateSections(facts, plan).IsError.ShouldBeFalse();
        var ready = FareSummaryFormatter.Current(
            snapshot,
            Now,
            hasSelectedServices: true,
            plan: plan
        );
        ready.Presentation.ShouldBe("AiOrganized");
        ready.Notices.ShouldContain(x => x.Contains("дополнительного багажа"));
        var late = FareSummaryFormatter.Current(snapshot, Now.AddHours(1), plan: plan);
        late.State.ShouldBe("Stale");
        late.Presentation.ShouldBe("Deterministic");
        late.AiStatus.ShouldBe("SourceStale");
        var merged = new[]
        {
            new FareSummaryPlanSection("change", [new FareSummaryPlanGroup(["f0", "f2"])]),
            new FareSummaryPlanSection("refund", [new FareSummaryPlanGroup(["f1"])]),
        };
        FareSummaryPlanValidator.ValidateSections(facts, merged).IsError.ShouldBeTrue();
        FareSummaryFormatter
            .Current(snapshot, Now, plan: merged)
            .Presentation.ShouldBe("Deterministic");
        FareSummaryPlanValidator
            .ValidateSections(
                facts,
                [new FareSummaryPlanSection("change", [new FareSummaryPlanGroup(["f0"])])]
            )
            .IsError.ShouldBeTrue();
        FareSummaryPlanValidator
            .ValidateSections(
                facts,
                [new FareSummaryPlanSection("change", [new FareSummaryPlanGroup(["f0", "f0"])])]
            )
            .IsError.ShouldBeTrue();
        var requested = new FareSummaryRequested(
            Guid.NewGuid(),
            snapshot.Fingerprint,
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            Now.AddSeconds(2),
            facts
        );
        var response = new FareSummaryPlanned(
            requested.CorrelationId,
            requested.Fingerprint,
            requested.PromptVersion,
            "ru",
            "Ready",
            plan
        );
        FareSummaryPlanValidator.Validate(requested, response).IsError.ShouldBeFalse();
        FareSummaryPlanValidator
            .Validate(requested, response with { CorrelationId = Guid.NewGuid() })
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Equal_leg_group_keeps_fee_per_leg_and_exact_scope_union()
    {
        var fee = FareCondition
            .Create(
                FarePermission.Allowed,
                Money.Create(12.5m, CurrencyCode.Create("GBP").Value).Value,
                "Known",
                "Known"
            )
            .Value;
        var evidence = FareRuleEvidence
            .Create(
                Now,
                Now.AddHours(1),
                Now.AddDays(1),
                1,
                new[] { 1, 1 },
                FareCondition.NotProvided,
                FareCondition.NotProvided,
                new[] { new FareLegFact(0, null, fee), new FareLegFact(1, null, fee) },
                new[]
                {
                    new FareSegmentFact(0, 0, 0, null, null, null, null),
                    new FareSegmentFact(1, 0, 0, null, null, null, null),
                }
            )
            .Value;
        var snapshot = JsonSerializer.Deserialize<QuotedFareSnapshot>(
            JsonSerializer.Serialize(
                new
                {
                    AggregateId = Guid.NewGuid(),
                    OfferId = Guid.NewGuid(),
                    QuoteRevision = Guid.NewGuid(),
                    FingerprintVersion = 1,
                    Fingerprint = new string('a', 64),
                    Evidence = evidence,
                }
            )
        )!;
        var summary = FareSummaryFormatter.Current(snapshot, Now);
        var grouped = summary
            .Sections.Single(x => x.Kind == "change")
            .Lines.Single(x => x.FactIds.Length == 2);
        grouped.Scope.ShouldContain("1, 2");
        grouped.Text.ShouldContain("для каждого указанного участка");
        grouped.Text.ShouldContain("12.5 GBP");
    }

    internal static QuotedFareSnapshot Snapshot(
        FareCondition? root = null,
        FareCondition? leg = null,
        int adults = 1
    )
    {
        var evidence = FareRuleEvidence
            .Create(
                Now,
                Now.AddHours(1),
                Now.AddDays(1),
                adults,
                new[] { 1 },
                root ?? FareCondition.NotProvided,
                FareCondition.NotProvided,
                new[]
                {
                    new FareLegFact(0, "Secret supplier brand", leg ?? FareCondition.NotProvided),
                },
                Enumerable
                    .Range(0, adults)
                    .Select(adult => new FareSegmentFact(
                        0,
                        0,
                        adult,
                        "Secret fare basis",
                        null,
                        0,
                        null
                    ))
            )
            .Value;
        return JsonSerializer.Deserialize<QuotedFareSnapshot>(
            JsonSerializer.Serialize(
                new
                {
                    AggregateId = Guid.NewGuid(),
                    OfferId = Guid.NewGuid(),
                    QuoteRevision = Guid.NewGuid(),
                    FingerprintVersion = 1,
                    Fingerprint = new string('a', 64),
                    Evidence = evidence,
                }
            )
        )!;
    }

    [Fact]
    public void Unsupported_history_preserves_parent_count_and_selected_extras_notice()
    {
        var snapshot = JsonSerializer.Deserialize<QuotedFareSnapshot>(
            JsonSerializer
                .Serialize(Snapshot(adults: 9))
                .Replace("\"SchemaVersion\":1", "\"SchemaVersion\":7")
        )!;
        var summary = FareSummaryFormatter.Historical(snapshot, Now, hasSelectedServices: true);
        summary.State.ShouldBe("Unavailable");
        summary.PassengerCount.ShouldBe(9);
        summary.Notices.ShouldContain(x => x.Contains("дополнительного багажа"));
    }
}
