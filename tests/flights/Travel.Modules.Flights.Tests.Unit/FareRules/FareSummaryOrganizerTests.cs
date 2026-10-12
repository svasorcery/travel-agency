using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class FareSummaryOrganizerTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    internal static QuotedFareSnapshot Snapshot(DateTimeOffset? expires = null)
    {
        var party = BookableOfferParty
            .Create(
                [
                    new(
                        SupplierPassengerReference.Create("fictional-adult").Value,
                        BookingPassengerKind.Adult
                    ),
                ],
                new DateOnly(2026, 10, 13),
                true,
                false
            )
            .Value;
        var binding = QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    new(
                        new BookingPassengerId(Guid.NewGuid()),
                        party.Passengers[0].Reference,
                        BookingPassengerKind.Adult
                    ),
                ]
            )
            .Value;
        var segment = Segment
            .Create(
                IataCode.Create("AAA").Value,
                IataCode.Create("BBB").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(2),
                "XX",
                "100",
                CabinClass.Economy
            )
            .Value;
        var itinerary = Itinerary.Create([Slice.Create([segment]).Value]).Value;
        var evidence = FareRuleEvidence
            .Create(
                Now,
                expires ?? Now.AddMinutes(20),
                Now.AddDays(1),
                1,
                [1],
                FareCondition.Create(FarePermission.Allowed, null, "Known", "NotProvided").Value,
                FareCondition.NotProvided,
                [new(0, "Fictional Flex", FareCondition.NotProvided)],
                [new(0, 0, 0, "Y", null, 0, null)]
            )
            .Value;
        var offer = new BookableOffer(
            OfferId.New(),
            itinerary,
            Money.Create(100m, CurrencyCode.Create("EUR").Value).Value,
            ProviderId.Duffel,
            Now,
            evidence.OfferExpiresAt,
            new(false, false, null, null),
            "fictional-offer",
            party,
            evidence
        );
        return QuotedFareSnapshot.Create(Guid.NewGuid(), offer, binding).Value;
    }

    public class BusProxy : DispatchProxy
    {
        public int Calls;
        public Func<object, object?> OnInvoke = _ => throw new InvalidOperationException();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            return OnInvoke(args![0]!);
        }
    }

    private static (FareSummaryOrganizer Service, BusProxy Bus, FakeTimeProvider Time) Create(
        bool enabled = true
    )
    {
        var bus = DispatchProxy.Create<IMessageBus, BusProxy>();
        var proxy = (BusProxy)bus;
        var time = new FakeTimeProvider(Now);
        proxy.OnInvoke = message => Task.FromResult(Reply((FareSummaryRequested)message));
        return (
            new(
                bus,
                time,
                new() { OrganizedEnabled = enabled },
                NullLogger<FareSummaryOrganizer>.Instance
            ),
            proxy,
            time
        );
    }

    private static FareSummaryPlanned Reply(FareSummaryRequested r) =>
        new(
            r.CorrelationId,
            r.Fingerprint,
            r.PromptVersion,
            r.Locale,
            "Ready",
            r.Facts.GroupBy(f => f.Section)
                .Select(section => new FareSummaryPlanSection(
                    section.Key,
                    section.Select(f => new FareSummaryPlanGroup([f.Id])).ToArray()
                ))
                .ToArray()
        );

    [Theory]
    [InlineData(null, "NotRequested")]
    [InlineData("deterministic", "NotRequested")]
    [InlineData("other", "UnsupportedMode")]
    public async Task Only_explicit_organized_mode_dispatches(string? mode, string expected)
    {
        var (service, bus, _) = Create();
        var result = await service.OrganizeAsync(
            Snapshot(),
            mode,
            true,
            false,
            Now,
            _ => throw new InvalidOperationException("No read"),
            TestContext.Current.CancellationToken
        );
        result.AiStatus.ShouldBe(expected);
        bus.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Ready_plan_requires_one_dispatch_and_fresh_committed_source()
    {
        var (service, bus, _) = Create();
        var reads = 0;
        var result = await service.OrganizeAsync(
            Snapshot(),
            "organized",
            true,
            false,
            Now,
            _ =>
            {
                reads++;
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken
        );
        result.State.ShouldBe("Current");
        result.Presentation.ShouldBe("AiOrganized");
        bus.Calls.ShouldBe(1);
        reads.ShouldBe(1);
        result.Notices.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Deadline_includes_handler_budget_and_never_retries_timeout()
    {
        var (service, bus, _) = Create();
        bus.OnInvoke = _ => throw new TimeoutException();
        var result = await service.OrganizeAsync(
            Snapshot(),
            "organized",
            true,
            false,
            Now.AddSeconds(-11),
            _ => Task.FromResult(true),
            TestContext.Current.CancellationToken
        );
        result.Presentation.ShouldBe("Deterministic");
        result.AiStatus.ShouldBe("DeadlineExceeded");
        bus.Calls.ShouldBe(1);
        var (service2, bus2, _) = Create();
        (
            await service2.OrganizeAsync(
                Snapshot(),
                "organized",
                true,
                false,
                Now.AddSeconds(-12),
                _ => Task.FromResult(true),
                TestContext.Current.CancellationToken
            )
        ).AiStatus.ShouldBe("DeadlineExceeded");
        bus2.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Late_reply_after_offer_expiry_has_only_stale_deterministic_facts()
    {
        var (service, bus, time) = Create();
        bus.OnInvoke = message =>
        {
            time.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult(Reply((FareSummaryRequested)message));
        };
        var result = await service.OrganizeAsync(
            Snapshot(Now.AddSeconds(1)),
            "organized",
            true,
            false,
            Now,
            _ => throw new InvalidOperationException("Expired source must not read"),
            TestContext.Current.CancellationToken
        );
        result.State.ShouldBe("Stale");
        result.Reason.ShouldBe("Expired");
        result.Presentation.ShouldBe("Deterministic");
        bus.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, "Superseded")]
    [InlineData(true, "FreshnessUnknown")]
    public async Task Competing_mutation_or_failed_final_read_cannot_claim_current(
        bool fails,
        string reason
    )
    {
        var (service, bus, _) = Create();
        var result = await service.OrganizeAsync(
            Snapshot(),
            "organized",
            true,
            false,
            Now,
            _ => fails ? throw new InvalidOperationException() : Task.FromResult(false),
            TestContext.Current.CancellationToken
        );
        result.State.ShouldBe("Stale");
        result.Reason.ShouldBe(reason);
        result.Presentation.ShouldBe("Deterministic");
        bus.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Stale_correlation_and_omitted_claims_fall_back_without_second_call()
    {
        var (service, bus, _) = Create();
        bus.OnInvoke = message =>
            Task.FromResult(
                Reply((FareSummaryRequested)message) with
                {
                    CorrelationId = Guid.NewGuid(),
                }
            );
        var result = await service.OrganizeAsync(
            Snapshot(),
            "organized",
            true,
            false,
            Now,
            _ => Task.FromResult(true),
            TestContext.Current.CancellationToken
        );
        result.AiStatus.ShouldBe("InvalidOutput");
        result.Presentation.ShouldBe("Deterministic");
        bus.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Already_cancelled_browser_never_dispatches()
    {
        var (service, bus, _) = Create();
        using var browser = new CancellationTokenSource();
        browser.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            service.OrganizeAsync(
                Snapshot(),
                "organized",
                true,
                false,
                Now,
                _ => Task.FromResult(true),
                browser.Token
            )
        );
        bus.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Anonymous_organized_request_never_dispatches()
    {
        var bus = DispatchProxy.Create<IMessageBus, BusProxy>();
        var time = new FakeTimeProvider(Now);
        var service = new FareSummaryOrganizer(
            bus,
            time,
            new() { OrganizedEnabled = true },
            NullLogger<FareSummaryOrganizer>.Instance
        );
        var result = await service.OrganizeAsync(
            Snapshot(),
            "organized",
            false,
            false,
            Now,
            _ => Task.FromResult(true),
            TestContext.Current.CancellationToken
        );
        result.AiStatus.ShouldBe("NotAuthenticated");
        ((BusProxy)bus).Calls.ShouldBe(0);
        result.Presentation.ShouldBe("Deterministic");
    }
}
