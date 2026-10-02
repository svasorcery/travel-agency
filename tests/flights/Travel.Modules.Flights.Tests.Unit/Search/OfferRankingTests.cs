using Shouldly;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Search;

public sealed class OfferRankingTests
{
    private static RankingCandidate Candidate(Offer o) =>
        new(
            o,
            o.TotalAmount,
            o.TotalAmount.Currency == Rub
                ? RankingPriceState.Native
                : RankingPriceState.FxUnavailable
        );

    private static IReadOnlyList<Offer> RankOffers(IEnumerable<Offer> offers, int top = 200) =>
        OfferRanker.Rank(offers.Select(Candidate), Rub, top).Offers;

    private static IReadOnlyList<Offer> DedupOffers(IEnumerable<Offer> offers) =>
        OfferDeduplicator.Dedup(offers.Select(Candidate)).Select(c => c.Offer).ToArray();

    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;
    private static readonly DateTimeOffset Now = new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<Offer> Rank(params Offer[] offers) => RankOffers(offers);

    [Fact]
    public void Different_currencies_have_separate_comparison_groups()
    {
        var rub = Book("rub", 5000);
        var eur = Book("eur", 100) with
        {
            TotalAmount = Money.Create(100, CurrencyCode.Create("EUR").Value).Value,
        };
        Rank(eur, rub).Select(o => o.TotalAmount.Currency.Value).ShouldBe(new[] { "RUB", "EUR" });
    }

    [Fact]
    public void Synthetic_partner_duration_does_not_beat_known_duration_on_equal_price()
    {
        var book = Book("book", 5000, hours: 2);
        var partner = Partner(Book("partner", 5000, hours: 1));
        Rank(partner, book)[0].ShouldBe(book);
    }

    [Fact]
    public void Fewer_transfers_break_equal_price_and_duration_ties()
    {
        var direct = Book("direct", 5000, id: "ffffffff-ffff-ffff-ffff-ffffffffffff");
        var connecting = Book(
            "connecting",
            5000,
            connection: true,
            id: "00000000-0000-0000-0000-000000000001"
        );
        Rank(connecting, direct)[0].ShouldBe(direct);
    }

    [Fact]
    public void Fresh_generated_ids_do_not_change_semantic_tie_order()
    {
        var a = Book("a", 5000, id: "00000000-0000-0000-0000-000000000001");
        var b = Book("b", 5000, id: "ffffffff-ffff-ffff-ffff-ffffffffffff");
        var first = Rank(a, b).Cast<BookableOffer>().Select(o => o.ProviderOfferRef).ToArray();
        var second = Rank(a with { Id = b.Id }, b with { Id = a.Id })
            .Cast<BookableOffer>()
            .Select(o => o.ProviderOfferRef)
            .ToArray();
        second.ShouldBe(first);
    }

    [Fact]
    public void Distinct_purchase_paths_refs_and_prices_survive_dedup()
    {
        var a = Book("a", 5000);
        var b = Book("b", 5000);
        var cheaper = a with { TotalAmount = Money.Create(4000, Rub).Value };
        DedupOffers(new Offer[] { a, b, cheaper, Partner(a) }).Count.ShouldBe(4);
    }

    [Fact]
    public void Exact_duplicates_keep_the_newest_snapshot()
    {
        var old = Book("a", 5000, id: "00000000-0000-0000-0000-000000000001");
        var fresh = old with { Id = OfferId.New(), FetchedAt = Now.AddMinutes(1) };
        DedupOffers(new Offer[] { old, fresh }).Single().ShouldBe(fresh);
    }

    private static DeeplinkOffer Partner(BookableOffer source) =>
        new(
            OfferId.New(),
            source.Itinerary,
            source.TotalAmount,
            ProviderId.Travelpayouts,
            Now,
            new Uri("https://example.invalid/flight"),
            "Fictional partner"
        );

    [Fact]
    public void Metadata_explains_known_and_unknown_factors_and_is_valid()
    {
        var book = Book("a", 5000);
        var partner = Partner(book);
        var ranked = OfferRanker.Rank(new[] { Candidate(partner), Candidate(book) }, Rub);
        ranked.Ranking.Entries[0].DurationSeconds.ShouldBe(7200);
        ranked.Ranking.Entries[0].Transfers.ShouldBe(0);
        ranked.Ranking.Entries[1].DurationSeconds.ShouldBeNull();
        ranked.Ranking.Entries[1].Transfers.ShouldBeNull();
        ranked.Ranking.Entries[1].Limitations.ShouldBe(new[] { "partial-itinerary" });
        OfferRanker.IsValid(ranked.Ranking, ranked.Offers).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    public void Cap_is_applied_after_ordering_and_ranks_are_contiguous(int count, int expected)
    {
        var ranked = OfferRanker.Rank(
            Enumerable.Range(0, count).Reverse().Select(i => Candidate(Book($"ref-{i}", i))),
            Rub
        );
        ranked.Offers.Count.ShouldBe(expected);
        ranked.Ranking.Entries.Select(e => e.Rank).ShouldBe(Enumerable.Range(1, expected));
        ranked
            .Offers.Select(o => o.TotalAmount.Amount)
            .ShouldBe(Enumerable.Range(0, expected).Select(i => (decimal)i));
        OfferRanker.IsValid(ranked.Ranking, ranked.Offers).ShouldBeTrue();
    }

    [Fact]
    public void Unknowns_and_ties_keep_the_same_order_for_every_permutation()
    {
        Offer[] offers =
        [
            Book("b", 5000),
            Book("a", 5000),
            Partner(Book("p", 5000)),
            Book("cheap", 4000),
        ];
        var expected = RankOffers(offers).Select(o => o.Id).ToArray();
        foreach (var first in offers)
        foreach (var second in offers.Where(o => o != first))
        foreach (var third in offers.Where(o => o != first && o != second))
        {
            var fourth = offers.Single(o => o != first && o != second && o != third);
            Rank(first, second, third, fourth).Select(o => o.Id).ShouldBe(expected);
        }
    }

    [Fact]
    public void Fare_changes_and_connections_are_not_discarded()
    {
        var book = Book("a", 5000);
        var fare = book with { FareConditions = book.FareConditions with { RefundAllowed = true } };
        var connecting = Book("a", 5000, connection: true);
        DedupOffers(new[] { book, fare, connecting }).Count.ShouldBe(3);
    }

    [Fact]
    public void Cache_identity_includes_locale_and_policy_namespace()
    {
        SearchCriteria Criteria(string locale) =>
            SearchCriteria
                .Create(
                    IataCode.Create("LED").Value,
                    IataCode.Create("DME").Value,
                    new DateOnly(2031, 2, 1),
                    null,
                    1,
                    CabinClass.Economy,
                    Rub,
                    locale
                )
                .Value;
        SearchCacheKey.Build(Criteria("ru")).ShouldStartWith("flights:search:v2:price-first-v1:");
        SearchCacheKey.Build(Criteria("ru")).ShouldNotBe(SearchCacheKey.Build(Criteria("en")));
    }

    internal static BookableOffer Book(
        string reference,
        decimal amount,
        int hours = 2,
        bool connection = false,
        string? id = null
    )
    {
        var led = IataCode.Create("LED").Value;
        var svo = IataCode.Create("SVO").Value;
        var dme = IataCode.Create("DME").Value;
        var departure = Now.AddDays(30);
        var segments = connection
            ? new[]
            {
                Segment
                    .Create(
                        led,
                        svo,
                        departure,
                        departure.AddMinutes(30),
                        "ZZ",
                        "101",
                        CabinClass.Economy
                    )
                    .Value,
                Segment
                    .Create(
                        svo,
                        dme,
                        departure.AddMinutes(60),
                        departure.AddHours(hours),
                        "ZZ",
                        "102",
                        CabinClass.Economy
                    )
                    .Value,
            }
            : new[]
            {
                Segment
                    .Create(
                        led,
                        dme,
                        departure,
                        departure.AddHours(hours),
                        "ZZ",
                        "101",
                        CabinClass.Economy
                    )
                    .Value,
            };
        return new BookableOffer(
            id is null ? OfferId.New() : new OfferId(Guid.Parse(id)),
            Itinerary.Create([Slice.Create(segments).Value]).Value,
            Money.Create(amount, Rub).Value,
            ProviderId.Duffel,
            Now,
            Now.AddHours(1),
            new FareConditions(false, false, "DEMO", "Economy"),
            reference
        );
    }
}
