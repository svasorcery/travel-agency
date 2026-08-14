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
        fixture.AiListenerMembership.Subject.ShouldBe("travel.ai.nl_search");
        fixture.AiListenerMembership.QueueGroup.ShouldBe("travel.ai.nl_search.workers");
        fixture.AiListenerMembership.SubscriptionCount.ShouldBe(2);
        fixture.AiListenerMembership.DistinctConnectionCount.ShouldBe(2);

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
        var survivorMembership = await fixture.WaitForAiListenerMembershipAsync(
            expectedSubscriptionCount: 1,
            overallCts.Token
        );
        survivorMembership.Subject.ShouldBe("travel.ai.nl_search");
        survivorMembership.QueueGroup.ShouldBe("travel.ai.nl_search.workers");
        survivorMembership.SubscriptionCount.ShouldBe(1);
        survivorMembership.DistinctConnectionCount.ShouldBe(1);

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
        var stoppedMembership = await fixture.WaitForAiListenerMembershipAsync(
            expectedSubscriptionCount: 0,
            overallCts.Token
        );
        stoppedMembership.Subject.ShouldBe("travel.ai.nl_search");
        stoppedMembership.QueueGroup.ShouldBe("travel.ai.nl_search.workers");
        stoppedMembership.SubscriptionCount.ShouldBe(0);
        stoppedMembership.DistinctConnectionCount.ShouldBe(0);

        var fallback = await fixture.InvokeHostHandlerWithoutAiAsync(overallCts.Token);
        fallback.IsError.ShouldBeTrue();
        fallback.FirstError.Code.ShouldBe(FlightsErrors.NlSearchUnparseable.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"total":"1","subscriptions_list":[]}""")]
    [InlineData("""{"total":1,"subscriptions_list":{}}""")]
    [InlineData("""{"total":1,"subscriptions_list":[null]}""")]
    [InlineData("""{"total":1,"subscriptions_list":[{}]}""")]
    [InlineData("""{"total":1,"subscriptions_list":[{"subject":7}]}""")]
    [InlineData(
        """{"total":1,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":1,"qgroup":7}]}"""
    )]
    [InlineData(
        """{"total":1,"subscriptions_list":[{"subject":"travel.ai.nl_search","qgroup":"travel.ai.nl_search.workers"}]}"""
    )]
    [InlineData(
        """{"total":1,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":"1","qgroup":"travel.ai.nl_search.workers"}]}"""
    )]
    [InlineData(
        """{"total":2,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":1,"qgroup":"travel.ai.nl_search.workers"}]}"""
    )]
    public void Nats_monitor_membership_parser_rejects_invalid_schema(string json)
    {
        Should.Throw<InvalidOperationException>(() =>
            NlSearchTransportFixture.TryReadAiListenerMembership(json, out _)
        );
    }

    [Fact]
    public void Nats_monitor_membership_accepts_omitted_list_only_for_zero_expected()
    {
        var ready = NlSearchTransportFixture.TryReadAiListenerMembership(
            """{"total":0}""",
            expectedSubscriptionCount: 0,
            out var membership
        );

        ready.ShouldBeTrue();
        membership.ShouldNotBeNull();
        membership.SubscriptionCount.ShouldBe(0);
        membership.DistinctConnectionCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(
        """{"total":3,"subscriptions_list":[{"subject":"travel.ai.>"},{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":12,"qgroup":"travel.ai.nl_search.workers"}]}""",
        true
    )]
    [InlineData("""{"total":0}""", false)]
    [InlineData(
        """{"total":1,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"}]}""",
        false
    )]
    [InlineData(
        """{"total":3,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":12,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":13}]}""",
        false
    )]
    [InlineData(
        """{"total":3,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":12,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":13,"qgroup":"other-workers"}]}""",
        false
    )]
    [InlineData(
        """{"total":3,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":12,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":13,"qgroup":"travel.ai.nl_search.workers"}]}""",
        false
    )]
    [InlineData(
        """{"total":2,"subscriptions_list":[{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"},{"subject":"travel.ai.nl_search","cid":11,"qgroup":"travel.ai.nl_search.workers"}]}""",
        false
    )]
    public void Nats_monitor_membership_requires_exactly_two_grouped_subject_subscriptions(
        string json,
        bool expectedReady
    )
    {
        var ready = NlSearchTransportFixture.TryReadAiListenerMembership(json, out var membership);

        ready.ShouldBe(expectedReady);
        if (expectedReady)
        {
            membership.ShouldNotBeNull();
            membership.Subject.ShouldBe("travel.ai.nl_search");
            membership.QueueGroup.ShouldBe("travel.ai.nl_search.workers");
            membership.SubscriptionCount.ShouldBe(2);
            membership.DistinctConnectionCount.ShouldBe(2);
        }
        else
        {
            membership.ShouldBeNull();
        }
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
