using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelAggregationReviewTests
{
    [Fact]
    public void Semantic_ranking_fingerprint_does_not_depend_on_decimal_display_precision()
    {
        var original = Offers("hotelbeds", 1, 1)[0];
        var equivalent = original with
        {
            Terms = original.Terms with { Total = new(200.00m, "GBP") },
            Price = original.Price! with { SupplierPayable = new(200.00m, "GBP") },
        };
        HotelCostRanking.Fingerprint(equivalent).ShouldBe(HotelCostRanking.Fingerprint(original));
    }

    [Fact]
    public async Task Equivalent_duplicate_source_key_does_not_become_conflict_due_to_decimal_scale()
    {
        var original = Offers("hotelbeds", 1, 1)[0];
        var equivalent = original with
        {
            Terms = original.Terms with { Total = new(200.00m, "GBP") },
            Price = original.Price! with { SupplierPayable = new(200.00m, "GBP") },
        };
        var result = await new HotelSearchService(
            [new OwnedSource("hotelbeds", [original, equivalent])],
            new HotelSnapshotCache(TimeProvider.System),
            TimeProvider.System
        ).SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Complete);
        result.Value.Result.Items.Length.ShouldBe(1);
    }

    [Fact]
    public async Task Two_valid_source_inventories_are_capped_as_explicitly_limited_aggregate()
    {
        var sources = new[] { Source("duffel-stays", 100, 2), Source("hotelbeds", 100, 2) };
        var cache = new HotelSnapshotCache(TimeProvider.System);
        var result = await new HotelSearchService(sources, cache, TimeProvider.System).SearchAsync(
            Request(),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.Groups.Length.ShouldBeLessThanOrEqualTo(100);
        result.Value.Result.Items.Length.ShouldBeLessThanOrEqualTo(200);
        result.Value.Result.Items.ShouldNotBeEmpty();
        result.Value.Statuses.Length.ShouldBe(2);
    }

    [Fact]
    public async Task Combined_byte_overflow_reduces_whole_groups_instead_of_losing_all_inventory()
    {
        var sources = new[]
        {
            Source("duffel-stays", 40, 2, new string('Ж', 1500)),
            Source("hotelbeds", 40, 2, new string('Ж', 1500)),
        };
        var cache = new HotelSnapshotCache(TimeProvider.System);
        var result = await new HotelSearchService(sources, cache, TimeProvider.System).SearchAsync(
            Request(),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.Result.Items.ShouldNotBeEmpty();
        result.Value.Statuses.Length.ShouldBe(2);
        cache.PayloadBytes.ShouldBeLessThanOrEqualTo(2 * 1024 * 1024);
        foreach (var group in result.Value.Groups)
            group.Offers.Length.ShouldBe(2);
    }

    [Fact]
    public async Task Expired_source_offer_cannot_discard_the_other_sources_fresh_result()
    {
        var old = Offers("duffel-stays", 1, 1)[0] with
        {
            SourceExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        var good = Offers("hotelbeds", 1, 1)[0];
        var result = await new HotelSearchService(
            [new OwnedSource("duffel-stays", [old]), new OwnedSource("hotelbeds", [good])],
            new HotelSnapshotCache(TimeProvider.System),
            TimeProvider.System
        ).SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.Result.Items.Single().Provider.ShouldBe("hotelbeds");
        result
            .Value.Statuses.Single(s => s.Provider == "duffel-stays")
            .Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Conflicting_duplicate_source_key_is_quarantined_without_selecting_cheapest_version()
    {
        var original = Offers("hotelbeds", 1, 1)[0];
        var conflicting = original with
        {
            Terms = original.Terms with { Total = new(190, "GBP") },
            Price = original.Price! with { SupplierPayable = new(190, "GBP") },
        };
        var result = await new HotelSearchService(
            [new OwnedSource("hotelbeds", [original, conflicting])],
            new HotelSnapshotCache(TimeProvider.System),
            TimeProvider.System
        ).SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.Result.Items.ShouldBeEmpty();
        result.Value.Statuses.Single().Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.Statuses.Single().Reason.ShouldNotBeNull();
    }

    private static OwnedSource Source(
        string key,
        int properties,
        int variants,
        string? condition = null
    ) => new(key, Offers(key, properties, variants, condition));

    private static HotelOffer[] Offers(
        string key,
        int properties,
        int variants,
        string? condition = null
    )
    {
        var dates = Request().Dates;
        return Enumerable
            .Range(0, properties)
            .SelectMany(property =>
                Enumerable
                    .Range(0, variants)
                    .Select(rate =>
                    {
                        var id = $"owned-{property}-{rate}";
                        var terms = ReservationStateTests.Terms() with
                        {
                            Property = new(
                                $"owned-{property}",
                                "Owned fictional house",
                                "1 Owned Square",
                                null,
                                null
                            ),
                            Dates = dates,
                            RateId = id,
                            Conditions = condition is null ? [] : [new(condition, "WholeStay")],
                            Total = new(200 + property + rate, "GBP"),
                        };
                        return new HotelOffer(key, "Test", id, terms, null, true, null)
                        {
                            Source = new(key, "owned-review", "Test", "Fictional"),
                            Price = new(
                                "SupplierCost",
                                terms.Total,
                                null,
                                "WholeStay",
                                key,
                                [],
                                []
                            ),
                            RoomIdentity = "owned-room",
                            SelectionContext = new(2, "opaque-" + id),
                        };
                    })
            )
            .ToArray();
    }

    private static StaySearch Request()
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        return new(HotelSearchService.Areas[3], new(checkIn, checkIn.AddDays(2)), new(1));
    }

    private sealed class OwnedSource(string key, HotelOffer[] offers) : IHotelSearchProvider
    {
        public string Key => key;

        public CapabilitySupport GetSupport(StaySearch request) => CapabilitySupport.Yes;

        public Task<HotelExecution<HotelOffer>> SearchAsync(
            StaySearch request,
            CancellationToken ct
        ) => Task.FromResult(new HotelExecution<HotelOffer>(ExecutionKind.Complete, offers));

        public Task<HotelExecution<HotelOffer>> LoadRatesAsync(
            SelectedStay selection,
            CancellationToken ct
        ) => SearchAsync(selection.Request, ct);
    }
}
