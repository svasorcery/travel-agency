using System.Text.RegularExpressions;
using Shouldly;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Xunit;

namespace Travel.Host.Tests.Integration.FareSummary;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class FareSummaryTransportTests
{
    [Fact(Timeout = 45_000)]
    public async Task Two_grouped_responders_deliver_one_v1_result_and_stopped_consumer_falls_back()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        await using var fixture = await FareSummaryTransportFixture.StartAsync(
            "None",
            deadline.Token
        );
        var request = Request();
        var result = await fixture.InvokeAsync(request, deadline.Token);
        result.Subject.ShouldBe(FareSummaryMessageIdentity.Subject);
        result.Reply.Status.ShouldBe("Ready");
        FareSummaryPlanValidator.Validate(request, result.Reply).IsError.ShouldBeFalse();
        fixture.Probes.Sum(x => x.Calls).ShouldBe(1);
        fixture.Probes.Sum(x => x.Deliveries).ShouldBe(1);
        fixture
            .Probes.SelectMany(x => x.Ledger)
            .Single()
            .CorrelationId.ShouldBe(request.CorrelationId);
        // A real serialized response with a stale identity cannot become an accepted plan.
        FareSummaryPlanValidator
            .Validate(request, result.Reply with { CorrelationId = Guid.NewGuid() })
            .IsError.ShouldBeTrue();
        FareSummaryPlanValidator
            .Validate(request, result.Reply with { Fingerprint = new string('b', 64) })
            .IsError.ShouldBeTrue();
        await fixture.StopRespondersAsync(deadline.Token);
        var fallback = await fixture.OrganizeWithoutConsumersAsync(deadline.Token);
        fallback.Presentation.ShouldBe("Deterministic");
        fallback.AiStatus.ShouldNotBe("Ready");
        fixture.Probes.Sum(x => x.Calls).ShouldBe(1);
        fixture.Probes.Sum(x => x.Deliveries).ShouldBe(1);
    }

    [Theory(Timeout = 45_000)]
    [InlineData("AfterInference", 0)]
    [InlineData("AfterAccounting", 1)]
    [InlineData("AfterDelivery", 1)]
    public async Task Production_delivery_policy_does_not_reenter_after_ambiguous_model_accounting_or_reply(
        string fault,
        int expectedLedger
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        await using var fixture = await FareSummaryTransportFixture.StartAsync(
            fault,
            deadline.Token
        );
        try
        {
            await fixture.InvokeAsync(Request(), deadline.Token);
        }
        catch (TimeoutException) { }
        // Observe beyond the request timeout: any Wolverine automatic redelivery is a test failure.
        await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
        fixture.Probes.Sum(x => x.Deliveries).ShouldBe(1);
        fixture.Probes.Sum(x => x.Calls).ShouldBe(1);
        fixture.Probes.Sum(x => x.Ledger.Count).ShouldBe(expectedLedger);
    }

    [Fact]
    public void Full_process_sources_use_the_shared_v1_route_group_and_feature_delivery_policy()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        root.ShouldNotBeNull();
        var module = Regex.Replace(
            File.ReadAllText(
                Path.Combine(
                    root.FullName,
                    "modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs"
                )
            ),
            @"\s+",
            ""
        );
        module.ShouldContain(
            "options.PublishMessage<FareSummaryRequested>().ToNatsSubject(FareSummaryMessageIdentity.Subject)"
        );
        var ai = Regex.Replace(
            File.ReadAllText(Path.Combine(root.FullName, "apps/Travel.AI/Program.cs")),
            @"\s+",
            ""
        );
        ai.ShouldContain(
            "ListenToNatsSubject(FareSummaryMessageIdentity.Subject).UseQueueGroup(FareSummaryMessageIdentity.QueueGroup)"
        );
        ai.ShouldContain("opts.Policies.Add<FareSummaryDeliveryPolicy>()");
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
