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
    public async Task Host_and_ai_exchange_shared_nl_search_contract_over_core_nats()
    {
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        overallCts.CancelAfter(TimeSpan.FromSeconds(40));

        await using var fixture = await NlSearchTransportFixture.StartAsync(overallCts.Token);
        var correlationId = Guid.NewGuid();
        var request = new NlSearchRequested(
            "Хочу слетать из Петербурга в Москву 15 сентября",
            correlationId
        );

        var healthy = await fixture.InvokeHealthyAsync(request, overallCts.Token);

        healthy.ObservedSubject.ShouldBe("travel.ai.nl_search");
        healthy.Reply.ShouldBeOfType<NlSearchParsed>();
        healthy.Reply.CorrelationId.ShouldBe(request.CorrelationId);

        var ledgerRows = await fixture.ReadLedgerRowsAsync(correlationId, overallCts.Token);
        ledgerRows.Count.ShouldBe(1);
        ledgerRows[0].MessageIdentity.ShouldBe(NlSearchMessageIdentity.Requested);
        ledgerRows[0].CorrelationId.ShouldBe(request.CorrelationId);

        await fixture.StopAiAsync(overallCts.Token);

        var fallback = await fixture.InvokeHostHandlerWithoutAiAsync(overallCts.Token);
        fallback.IsError.ShouldBeTrue();
        fallback.FirstError.Code.ShouldBe(FlightsErrors.NlSearchUnparseable.Code);
    }
}
