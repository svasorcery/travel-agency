using Shouldly;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Core.Errors;
using Xunit;

namespace Travel.Host.Tests.Integration.NlSearch;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class NlSearchTransportTests
{
    [Fact(Timeout = 45_000)]
    public async Task Ai_replicas_load_balance_nl_search_and_fail_over_over_core_nats()
    {
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        overallCts.CancelAfter(TimeSpan.FromSeconds(40));

        await using var fixture = await NlSearchTransportFixture.StartAsync(overallCts.Token);
        var firstCorrelationId = Guid.NewGuid();
        var firstRequest = new NlSearchRequested(
            "Fly from Saint Petersburg to Moscow on September 15",
            firstCorrelationId
        );

        var firstReply = await fixture.InvokeHealthyAsync(firstRequest, overallCts.Token);

        firstReply.ObservedSubject.ShouldBe("travel.ai.nl_search");
        firstReply.Reply.ShouldBeOfType<NlSearchParsed>();
        firstReply.Reply.CorrelationId.ShouldBe(firstRequest.CorrelationId);

        await AssertSingleLedgerRowAsync(fixture, firstCorrelationId, overallCts.Token);

        var callsAfterFirstRequest = fixture.ReadReplicaCallCounts();
        callsAfterFirstRequest.Count.ShouldBe(2);
        callsAfterFirstRequest.Values.Sum().ShouldBe(1);
        callsAfterFirstRequest.ContainsKey(firstReply.Reply.ModelId).ShouldBeTrue();
        callsAfterFirstRequest[firstReply.Reply.ModelId].ShouldBe(1);

        await fixture.StopServingReplicaAsync(firstReply.Reply.ModelId, overallCts.Token);

        var secondCorrelationId = Guid.NewGuid();
        var secondRequest = new NlSearchRequested(
            "Fly from Moscow to Kazan on September 20",
            secondCorrelationId
        );

        var secondReply = await fixture.InvokeHealthyAsync(secondRequest, overallCts.Token);

        secondReply.ObservedSubject.ShouldBe("travel.ai.nl_search");
        secondReply.Reply.ShouldBeOfType<NlSearchParsed>();
        secondReply.Reply.CorrelationId.ShouldBe(secondRequest.CorrelationId);
        secondReply.Reply.ModelId.ShouldNotBe(firstReply.Reply.ModelId);

        await AssertSingleLedgerRowAsync(fixture, secondCorrelationId, overallCts.Token);

        var callsAfterFailover = fixture.ReadReplicaCallCounts();
        callsAfterFailover.Count.ShouldBe(2);
        callsAfterFailover.Values.Sum().ShouldBe(2);
        callsAfterFailover.Values.Count(count => count == 1).ShouldBe(2);

        await fixture.StopAllAiAsync(overallCts.Token);

        var fallback = await fixture.InvokeHostHandlerWithoutAiAsync(overallCts.Token);
        fallback.IsError.ShouldBeTrue();
        fallback.FirstError.Code.ShouldBe(FlightsErrors.NlSearchUnparseable.Code);
    }

    private static async Task AssertSingleLedgerRowAsync(
        NlSearchTransportFixture fixture,
        Guid correlationId,
        CancellationToken ct
    )
    {
        var ledgerRows = await fixture.ReadLedgerRowsAsync(correlationId, ct);
        ledgerRows.Count.ShouldBe(1);
        ledgerRows[0].MessageIdentity.ShouldBe(NlSearchMessageIdentity.Requested);
        ledgerRows[0].CorrelationId.ShouldBe(correlationId);
    }
}
