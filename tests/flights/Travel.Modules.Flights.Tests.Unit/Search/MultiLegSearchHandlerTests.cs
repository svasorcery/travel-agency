using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Application.Handlers.Search;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Search;

public sealed class MultiLegSearchHandlerTests
{
    internal static SearchCriteria Criteria(
        int count = 2,
        string currency = "USD",
        string locale = "ru"
    ) =>
        SearchCriteria
            .CreateMultiLeg(
                new[] { ("LED", "DME"), ("VKO", "DXB"), ("DXB", "SIN"), ("SIN", "LED") }
                    .Select(
                        (leg, index) =>
                            RequestedFlightLeg
                                .Create(
                                    IataCode.Create(leg.Item1).Value,
                                    IataCode.Create(leg.Item2).Value,
                                    new DateOnly(2030, 6, 1).AddDays(index * 3)
                                )
                                .Value
                    )
                    .ToArray(),
                count,
                CabinClass.Economy,
                CurrencyCode.Create(currency).Value,
                locale
            )
            .Value;

    internal static BookableOffer Offer(
        SearchCriteria criteria,
        string provider = "duffel",
        int hours = 2
    )
    {
        var slices = criteria
            .Legs.Select(leg =>
            {
                var departure = new DateTimeOffset(
                    leg.DepartureDate.ToDateTime(new TimeOnly(1, 0)),
                    TimeSpan.FromHours(3)
                );
                return Slice
                    .Create([
                        Segment
                            .Create(
                                leg.Origin,
                                leg.Destination,
                                departure,
                                departure.AddHours(hours),
                                "ZZ",
                                "101",
                                criteria.CabinClass
                            )
                            .Value,
                    ])
                    .Value;
            })
            .ToArray();
        return new(
            OfferId.New(),
            Itinerary.Create(slices).Value,
            Money.Create(100, criteria.Currency).Value,
            new(provider),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            new(false, false, null, null),
            "fictional",
            TestPii.Binding(criteria.PassengerCount, criteria.DepartureDate).Party
        );
    }

    private sealed class Provider(
        string id,
        IReadOnlyList<Offer> offers,
        FlightSearchSupport? support = null,
        bool fail = false
    ) : IFlightSearchProvider
    {
        public ProviderId Id => new(id);
        public int Calls;
        public int SupportCalls;
        public FlightSearchSupport Support = support ?? FlightSearchSupport.Available;

        public FlightSearchSupport GetSupport(SearchCriteria criteria)
        {
            SupportCalls++;
            return Support;
        }

        public Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria criteria,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult<ErrorOr<IReadOnlyList<Offer>>>(
                fail ? FlightsErrors.ProviderUnavailable(id) : offers.ToList()
            );
        }
    }

    private sealed class Cache : ISearchCache
    {
        public Dictionary<string, SearchResult> Values = [];
        public SearchResult? Forced;
        public Action? OnRead;

        public Task<SearchResult?> TryGetAsync(string key, CancellationToken ct)
        {
            OnRead?.Invoke();
            return Task.FromResult(Forced ?? Values.GetValueOrDefault(key));
        }

        public Task SetAsync(string key, SearchResult result, TimeSpan ttl, CancellationToken ct)
        {
            Values[key] = result;
            return Task.CompletedTask;
        }
    }

    private static Task<ErrorOr<SearchResult>> Run(
        SearchCriteria criteria,
        Cache cache,
        params IFlightSearchProvider[] providers
    ) =>
        SearchFlightsHandler.Handle(
            new(criteria),
            providers,
            cache,
            null!,
            new Metrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Support_is_evaluated_once_before_cache_lookup_on_cold_and_warm_search()
    {
        var criteria = Criteria();
        var provider = new Provider("duffel", [Offer(criteria)]);
        var cache = new Cache
        {
            OnRead = () => provider.SupportCalls.ShouldBeGreaterThan(provider.Calls),
        };
        var cold = await Run(criteria, cache, provider);
        var warm = await Run(criteria, cache, provider);
        cold.IsError.ShouldBeFalse();
        warm.Value.ShouldBe(cold.Value);
        provider.SupportCalls.ShouldBe(2);
        provider.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Adding_eligible_provider_invalidates_all_success_cache_and_searches_every_provider()
    {
        var criteria = Criteria();
        var cache = new Cache();
        var first = new Provider("duffel", [Offer(criteria)]);
        (await Run(criteria, cache, first)).IsError.ShouldBeFalse();
        var added = new Provider("additional", [Offer(criteria, "additional")]);
        var second = await Run(criteria, cache, first, added);
        first.Calls.ShouldBe(2);
        added.Calls.ShouldBe(1);
        second.Value.Offers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Removing_provider_invalidates_its_offers_and_failures()
    {
        var criteria = Criteria();
        var cache = new Cache();
        var first = new Provider("duffel", [Offer(criteria)]);
        var removed = new Provider("additional", [Offer(criteria, "additional")]);
        await Run(criteria, cache, first, removed);
        var result = await Run(criteria, cache, first);
        first.Calls.ShouldBe(2);
        result.Value.Offers.Single().Provider.Value.ShouldBe("duffel");
    }

    [Fact]
    public async Task Changing_support_invalidates_old_success_and_records_current_skips()
    {
        var criteria = Criteria();
        var cache = new Cache();
        var first = new Provider("duffel", [Offer(criteria)]);
        var other = new Provider("additional", [Offer(criteria, "additional")]);
        await Run(criteria, cache, first, other);
        other.Support = new(false, "journey-unsupported");
        var result = await Run(criteria, cache, first, other);
        result.Value.Offers.Count.ShouldBe(1);
        result.Value.SkippedProviders!.Single().Provider.ShouldBe("additional");
        first.Calls.ShouldBe(2);
        other.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("partyDate")]
    [InlineData("count")]
    [InlineData("airport")]
    [InlineData("date")]
    [InlineData("cabin")]
    public async Task Invalid_live_inventory_is_provider_failure_instead_of_fake_empty(
        string mismatch
    )
    {
        var criteria = Criteria();
        var invalid = InvalidOffer(criteria, mismatch);
        var result = await Run(criteria, new Cache(), new Provider("duffel", [invalid]));
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    [Theory]
    [InlineData("partyDate")]
    [InlineData("count")]
    [InlineData("airport")]
    [InlineData("date")]
    [InlineData("cabin")]
    public async Task Invalid_warm_inventory_is_a_miss(string mismatch)
    {
        var criteria = Criteria();
        var invalid = InvalidOffer(criteria, mismatch);
        var ranked = OfferRanker.Rank(
            [new(invalid, invalid.TotalAmount, RankingPriceState.Native)],
            criteria.Currency
        );
        var cache = new Cache { Forced = new(ranked.Offers, [], ranked.Ranking, []) };
        var provider = new Provider("duffel", [Offer(criteria)]);
        var result = await Run(criteria, cache, provider);
        result.IsError.ShouldBeFalse();
        provider.Calls.ShouldBe(1);
    }

    private static BookableOffer InvalidOffer(SearchCriteria criteria, string mismatch)
    {
        var offer = Offer(criteria);
        if (mismatch == "partyDate")
            return offer with
            {
                Party = TestPii.Binding(2, criteria.DepartureDate.AddDays(1)).Party,
            };
        if (mismatch == "count")
            return offer with { Party = TestPii.Binding(1, criteria.DepartureDate).Party };
        var legs = criteria.Legs.ToArray();
        if (mismatch == "airport")
            legs[3] = RequestedFlightLeg
                .Create(legs[3].Origin, IataCode.Create("SVO").Value, legs[3].DepartureDate)
                .Value;
        if (mismatch == "date")
            legs[3] = RequestedFlightLeg
                .Create(legs[3].Origin, legs[3].Destination, legs[3].DepartureDate.AddDays(1))
                .Value;
        var changed = SearchCriteria
            .CreateMultiLeg(
                legs,
                criteria.PassengerCount,
                mismatch == "cabin" ? CabinClass.Business : criteria.CabinClass,
                criteria.Currency
            )
            .Value;
        return Offer(changed);
    }

    [Fact]
    public void Key_distinguishes_exact_airport_mode_from_legacy_city_mode()
    {
        var multi = Criteria();
        var legacy = SearchCriteria
            .Create(
                multi.Origin,
                multi.Destination,
                multi.DepartureDate,
                null,
                multi.PassengerCount,
                multi.CabinClass,
                multi.Currency
            )
            .Value;
        var exact = SearchCriteria
            .CreateMultiLeg([multi.Legs[0]], multi.PassengerCount, multi.CabinClass, multi.Currency)
            .Value;
        SearchCacheKey.Build(exact).ShouldNotBe(SearchCacheKey.Build(legacy));
    }

    [Fact]
    public void Key_includes_leg_four_and_ordered_legs()
    {
        var criteria = Criteria();
        var legs = criteria.Legs.ToArray();
        legs[3] = RequestedFlightLeg
            .Create(legs[3].Origin, IataCode.Create("SVO").Value, legs[3].DepartureDate)
            .Value;
        var changed = SearchCriteria
            .CreateMultiLeg(legs, 2, criteria.CabinClass, criteria.Currency)
            .Value;
        SearchCacheKey.Build(criteria).ShouldNotBe(SearchCacheKey.Build(changed));
        var sameDay = criteria
            .Legs.Select(l =>
                RequestedFlightLeg.Create(l.Origin, l.Destination, criteria.DepartureDate).Value
            )
            .ToArray();
        var ordered = SearchCriteria
            .CreateMultiLeg(sameDay, 2, criteria.CabinClass, criteria.Currency)
            .Value;
        var reversed = SearchCriteria
            .CreateMultiLeg(sameDay.Reverse().ToArray(), 2, criteria.CabinClass, criteria.Currency)
            .Value;
        SearchCacheKey.Build(ordered).ShouldNotBe(SearchCacheKey.Build(reversed));
    }

    [Fact]
    public void Key_includes_currency_locale_and_party_count()
    {
        var key = SearchCacheKey.Build(Criteria());
        key.ShouldNotBe(SearchCacheKey.Build(Criteria(1)));
        key.ShouldNotBe(SearchCacheKey.Build(Criteria(currency: "RUB")));
        key.ShouldNotBe(SearchCacheKey.Build(Criteria(locale: "en")));
    }

    [Fact]
    public void Capability_inventory_identity_is_sorted_and_changes_when_provider_added_or_removed()
    {
        SearchProviderCapability[] one = [new("duffel", true, null)];
        SearchProviderCapability[] both =
        [
            .. one,
            new("travelpayouts", false, "journey-unsupported"),
        ];
        var key = SearchCacheKey.Build(Criteria(), one);
        key.ShouldNotBe(SearchCacheKey.Build(Criteria(), both));
        SearchCacheKey
            .Build(Criteria(), both)
            .ShouldBe(SearchCacheKey.Build(Criteria(), both.Reverse().ToArray()));
        key.ShouldNotBe(
            SearchCacheKey.Build(Criteria(), [new("duffel", false, "journey-unsupported")])
        );
        SearchCacheKey.Build(Criteria(), both).ShouldNotBe(SearchCacheKey.Build(Criteria(), one));
        key.ShouldNotBe(
            SearchCacheKey.Build(Criteria(), [new("duffel", false, "passenger-count-unsupported")])
        );
    }

    [Theory]
    [InlineData("skips")]
    [InlineData("offerProvider")]
    [InlineData("failureProvider")]
    public async Task Warm_result_must_agree_with_current_capability_inventory(string mismatch)
    {
        var criteria = Criteria();
        var offer = Offer(criteria);
        var ranked = OfferRanker.Rank(
            [new(offer, offer.TotalAmount, RankingPriceState.Native)],
            criteria.Currency
        );
        var stale = new SearchResult(
            ranked.Offers,
            mismatch == "failureProvider" ? [new("removed", "ProviderFailure", 1)] : [],
            ranked.Ranking,
            mismatch == "skips" ? [new("removed", "journey-unsupported")] : []
        );
        if (mismatch == "offerProvider")
            stale = stale with { Offers = [offer with { Provider = new("removed") }] };
        var provider = new Provider("duffel", [Offer(criteria)]);
        (await Run(criteria, new Cache { Forced = stale }, provider)).IsError.ShouldBeFalse();
        provider.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_provider_inventory_fails_whole_provider_while_valid_provider_survives_cold_and_warm()
    {
        var criteria = Criteria();
        var cache = new Cache();
        var invalid = new Provider(
            "invalid",
            [
                Offer(criteria, "invalid"),
                InvalidOffer(criteria, "date") with
                {
                    Provider = new("invalid"),
                },
            ]
        );
        var good = new Provider("duffel", [Offer(criteria)]);
        var cold = await Run(criteria, cache, invalid, good);
        var warm = await Run(criteria, cache, invalid, good);
        cold.IsError.ShouldBeFalse();
        cold.Value.Offers.Single().Provider.Value.ShouldBe("duffel");
        cold.Value.PartialFailures.Single().Provider.ShouldBe("invalid");
        cold.Value.PartialFailures.Single().ErrorCode.ShouldBe("Flights.ProviderUnavailable");
        warm.Value.ShouldBe(cold.Value);
        invalid.Calls.ShouldBe(1);
        good.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Removed_provider_failure_cannot_survive_new_composition()
    {
        var criteria = Criteria();
        var cache = new Cache();
        var first = new Provider("duffel", [Offer(criteria)]);
        await Run(criteria, cache, first, new Provider("removed", [], fail: true));
        var result = await Run(criteria, cache, first);
        result.Value.PartialFailures.ShouldBeEmpty();
        first.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task All_journey_ineligible_is_empty_with_capability_note_and_no_provider_call()
    {
        var provider = new Provider("travelpayouts", [], new(false, "journey-unsupported"));
        var result = await Run(Criteria(), new Cache(), provider);
        result.IsError.ShouldBeFalse();
        result.Value.Offers.ShouldBeEmpty();
        result.Value.SkippedProviders!.Single().ReasonCode.ShouldBe("journey-unsupported");
        provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Cached_all_eligible_provider_failures_is_a_miss_and_preserves_unavailable_semantics()
    {
        var criteria = Criteria();
        var cache = new Cache
        {
            Forced = new(
                [],
                [new("duffel", "ProviderFailure", 1)],
                OfferRanker.Rank([], criteria.Currency).Ranking,
                []
            ),
        };
        var provider = new Provider("duffel", [], fail: true);
        var result = await Run(criteria, cache, provider);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
        provider.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Cached_all_ineligible_with_current_skips_remains_valid_empty()
    {
        var criteria = Criteria();
        var cache = new Cache
        {
            Forced = new(
                [],
                [],
                OfferRanker.Rank([], criteria.Currency).Ranking,
                [new("travelpayouts", "journey-unsupported")]
            ),
        };
        var provider = new Provider("travelpayouts", [], new(false, "journey-unsupported"));
        var result = await Run(criteria, cache, provider);
        result.IsError.ShouldBeFalse();
        result.Value.Offers.ShouldBeEmpty();
        provider.Calls.ShouldBe(0);
        provider.SupportCalls.ShouldBe(1);
    }

    [Fact]
    public void Travelpayouts_legacy_single_passenger_remains_eligible()
    {
        var criteria = Criteria(1);
        var legacy = SearchCriteria
            .Create(
                criteria.Origin,
                criteria.Destination,
                criteria.DepartureDate,
                null,
                1,
                CabinClass.Economy,
                criteria.Currency
            )
            .Value;
        var provider = new TravelpayoutsSearchProvider(
            null!,
            null!,
            null!,
            null!,
            TimeProvider.System,
            null!,
            NullLogger<TravelpayoutsSearchProvider>.Instance
        );
        provider.GetSupport(legacy).Supported.ShouldBeTrue();
        provider.GetSupport(legacy).ReasonCode.ShouldBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Travelpayouts_explicit_route_is_unsupported_before_any_http_or_party_gate(
        int count
    )
    {
        var options = Options.Create(new TravelpayoutsOptions());
        // All dependencies are deliberately absent: capability rejection must precede their use.
        var provider = new TravelpayoutsSearchProvider(
            null!,
            options,
            null!,
            null!,
            TimeProvider.System,
            null!,
            NullLogger<TravelpayoutsSearchProvider>.Instance
        );
        provider.GetSupport(Criteria(count)).ReasonCode.ShouldBe("journey-unsupported");
        var result = await provider.SearchAsync(
            Criteria(count),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.ShouldBeEmpty();
    }

    private sealed class Metrics : IFlightsMetrics
    {
        public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

        public void RecordSearchError(string provider) { }

        public void RecordPaymentOutcome(bool success) { }

        public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

        public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

        public void RecordWebhookReceived(string eventType) { }

        public void RecordWebhookProcessingLag(double ms, string eventType) { }

        public void RecordAirlineInitiatedChange() { }

        public void RecordPaymentDuration(double ms, string outcome) { }

        public void RecordNlSearchDuration(double ms) { }

        public void RecordSearchPartialFill(bool partial) { }

        public void RecordOfferShown() { }

        public void RecordOrderBooked() { }
    }
}
