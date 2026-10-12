using Shouldly;
using Travel.Host.Tests.Integration.NlSearch;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Xunit;

namespace Travel.Host.Tests.Integration.FareSummary;

// CI-only: the retained fixture starts actual Programs and applies existing migrations
// only to its disposable PostgreSQL database. Assembly PII fixture forbids local execution.
[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class FareSummaryProductionPipelineTests
{
    [Fact(Timeout = 60_000)]
    public async Task Actual_Host_and_two_AI_Programs_route_fare_v1_and_account_real_EF_usage_without_replaying()
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        bound.CancelAfter(TimeSpan.FromSeconds(55));
        await using var fixture = await NlSearchTransportFixture.StartAsync(
            bound.Token,
            fareEnabled: true
        );
        await fixture.AssertProductionFareLedgerAsync(bound.Token);
        var membership = await fixture.WaitForFareListenerMembershipAsync(2, bound.Token);
        membership.Subject.ShouldBe(FareSummaryMessageIdentity.Subject);
        membership.QueueGroup.ShouldBe(FareSummaryMessageIdentity.QueueGroup);
        membership.SubscriptionCount.ShouldBe(2);
        membership.DistinctConnectionCount.ShouldBe(2);

        var ready = Request();
        var first = await fixture.InvokeFareAsync(ready, bound.Token);
        first.Subject.ShouldBe(FareSummaryMessageIdentity.Subject);
        first.Reply.Status.ShouldBe("Ready");
        FareSummaryPlanValidator.Validate(ready, first.Reply).IsError.ShouldBeFalse();
        fixture.FareModelCalls.ShouldBe(1);
        await AssertLedgerAsync(fixture, ready.CorrelationId, first.Reply.ModelId!, bound.Token);

        fixture.SetFareResponseMode("InvalidOutput");
        var rejected = Request();
        var second = await fixture.InvokeFareAsync(rejected, bound.Token);
        second.Subject.ShouldBe(FareSummaryMessageIdentity.Subject);
        second.Reply.Status.ShouldBe("InvalidOutput");
        second.Reply.Sections.ShouldBeNull();
        fixture.FareModelCalls.ShouldBe(2);
        await AssertLedgerAsync(
            fixture,
            rejected.CorrelationId,
            second.Reply.ModelId!,
            bound.Token
        );

        fixture.SetFareResponseMode("UsageUnknown");
        var unknown = Request();
        var third = await fixture.InvokeFareAsync(unknown, bound.Token);
        third.Reply.Status.ShouldBe("UsageUnknown");
        third.Reply.CostUsd.ShouldBeNull();
        (await fixture.ReadFareLedgerAsync(unknown.CorrelationId, bound.Token)).ShouldBeEmpty();
        fixture.FareModelCalls.ShouldBe(3);

        // Receipt identity is independently checked at the actual production caller boundary.
        FareSummaryPlanValidator
            .Validate(ready, first.Reply with { Fingerprint = new string('b', 64) })
            .IsError.ShouldBeTrue();
        await fixture.StopAllAiAsync(bound.Token);
        (
            await fixture.WaitForFareListenerMembershipAsync(0, bound.Token)
        ).SubscriptionCount.ShouldBe(0);
        var fallback = await fixture.InvokeFareProducerWithoutAiAsync(bound.Token);
        fallback.Presentation.ShouldBe("Deterministic");
        fallback.AiStatus.ShouldNotBe("Ready");
        fixture.FareModelCalls.ShouldBe(3);
    }

    private static async Task AssertLedgerAsync(
        NlSearchTransportFixture fixture,
        Guid correlation,
        string model,
        CancellationToken ct
    )
    {
        var rows = await fixture.ReadFareLedgerAsync(correlation, ct);
        rows.Length.ShouldBe(1);
        var row = rows[0];
        row.Feature.ShouldBe("flights.fare_summary");
        row.MessageIdentity.ShouldBe(FareSummaryMessageIdentity.Requested);
        row.CorrelationId.ShouldBe(correlation);
        row.Model.ShouldBe(model);
        row.InputTokens.ShouldBe(10);
        row.OutputTokens.ShouldBe(5);
        row.CostUsd.ShouldBe(0.000015m);
        row.UserId.ShouldBeNull();
    }

    private static FareSummaryRequested Request() =>
        new(
            Guid.NewGuid(),
            new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            DateTimeOffset.UtcNow.AddSeconds(4),
            [
                new("f0", "change", "change", "Allowed", null, null, null, [new("offer")]),
                new("f1", "refund", "refund", "Unknown", null, null, null, [new("offer")]),
                new("f2", "change", "change", "Unknown", null, null, null, [new("leg", 0)]),
            ]
        );
}
