using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
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
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Search;

public sealed class SearchPartyTests
{
    private static SearchCriteria Criteria(int count = 2) =>
        SearchCriteria
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                new DateOnly(2027, 1, 1),
                null,
                count,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;

    private static BookableOffer Offer(int count = 2)
    {
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                new DateTimeOffset(2027, 1, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "1",
                CabinClass.Economy
            )
            .Value;
        return new(
            OfferId.New(),
            Itinerary.Create([Slice.Create([segment]).Value]).Value,
            Money.Create(100, CurrencyCode.Create("USD").Value).Value,
            ProviderId.Duffel,
            DateTimeOffset.UtcNow,
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new(false, false, null, null),
            "off_a",
            TestPii.Binding(count).Party
        );
    }

    private sealed class Provider(
        bool skipped = false,
        bool fail = false,
        IReadOnlyList<Offer>? offers = null
    ) : IFlightSearchProvider
    {
        public ProviderId Id => skipped ? ProviderId.Travelpayouts : ProviderId.Duffel;
        public int Calls;

        public FlightSearchSupport GetSupport(SearchCriteria c) =>
            skipped ? FlightSearchSupport.PassengerCountUnsupported : FlightSearchSupport.Available;

        public Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria c,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult<ErrorOr<IReadOnlyList<Offer>>>(
                fail ? FlightsErrors.ProviderUnavailable(Id.Value) : (offers ?? []).ToList()
            );
        }
    }

    private sealed class Cache : ISearchCache
    {
        public SearchResult? Value;

        public Task<SearchResult?> TryGetAsync(string key, CancellationToken ct) =>
            Task.FromResult(Value);

        public Task SetAsync(string key, SearchResult result, TimeSpan ttl, CancellationToken ct)
        {
            Value = result;
            return Task.CompletedTask;
        }
    }

    private static Task<ErrorOr<SearchResult>> Run(
        Cache cache,
        params IFlightSearchProvider[] providers
    ) =>
        SearchFlightsHandler.Handle(
            new(Criteria()),
            providers,
            cache,
            null!,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            default
        );

    [Fact]
    public async Task Cold_and_warm_preserve_skip_without_provider_call_or_price_multiplication()
    {
        var cache = new Cache();
        var provider = new Provider(offers: [Offer()]);
        var skip = new Provider(skipped: true);
        var cold = await Run(cache, provider, skip);
        var warm = await Run(cache, provider, skip);
        cold.IsError.ShouldBeFalse();
        JsonSerializer.Serialize(warm.Value).ShouldBe(JsonSerializer.Serialize(cold.Value));
        cold.Value.Offers.Single().TotalAmount.Amount.ShouldBe(100);
        skip.Calls.ShouldBe(0);
        provider.Calls.ShouldBe(1);
        cold.Value.SkippedProviders!.Single().ReasonCode.ShouldBe("passenger-count-unsupported");
    }

    [Fact]
    public async Task All_eligible_fail_with_skip_is_unavailable()
    {
        var result = await Run(new Cache(), new Provider(fail: true), new Provider(skipped: true));
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    [Fact]
    public async Task All_ineligible_is_explicit_empty_without_outage()
    {
        var skip = new Provider(skipped: true);
        var result = await Run(new Cache(), skip);
        result.IsError.ShouldBeFalse();
        result.Value.Offers.ShouldBeEmpty();
        result.Value.SkippedProviders!.Count.ShouldBe(1);
        skip.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Missing_skips_or_wrong_count_cache_is_a_miss(int count, bool skips)
    {
        var offer = Offer(count);
        var rank = OfferRanker.Rank(
            [new(offer, offer.TotalAmount, RankingPriceState.Native)],
            Criteria().Currency
        );
        var cache = new Cache { Value = new(rank.Offers, [], rank.Ranking, skips ? [] : null) };
        var provider = new Provider(offers: [Offer()]);
        var result = await Run(cache, provider);
        result.IsError.ShouldBeFalse();
        provider.Calls.ShouldBe(count == 2 && skips ? 0 : 1);
    }

    [Fact]
    public async Task Mismatched_cold_provider_party_cannot_escape_search_intent()
    {
        var result = await Run(new Cache(), new Provider(offers: [Offer(1)]));
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    [Fact]
    public void Dedup_uses_normalized_party_facts_but_not_generated_offer_ids()
    {
        var offer = Offer();
        var same = offer with { Id = OfferId.New() };
        var changed = offer with
        {
            Id = OfferId.New(),
            Party = BookableOfferParty
                .Create(offer.Party!.Passengers, offer.Party.FirstDepartureLocalDate, false, false)
                .Value,
        };
        OfferDeduplicator
            .Dedup(
                new[] { offer, same, changed }.Select(o => new RankingCandidate(
                    o,
                    o.TotalAmount,
                    RankingPriceState.Native
                ))
            )
            .Count.ShouldBe(2);
    }

    [Fact]
    public void Search_namespace_is_bumped() =>
        SearchCacheKey.Build(Criteria()).ShouldStartWith("flights:search:v4:");

    private sealed class NoOpMetrics : IFlightsMetrics
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
