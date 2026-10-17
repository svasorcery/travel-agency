using System.Text.Json;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelSearchTests
{
    [Fact]
    public void Cache_repeated_eviction_keeps_new_rates_instead_of_reusing_the_first_dictionary_slot()
    {
        var time = new FrozenCacheTime(DateTimeOffset.UtcNow);
        var cache = new HotelSnapshotCache(time, 2, 16 * 1024 * 1024);
        var a = Snapshot("old-a") with { ExpiresAt = time.GetUtcNow().AddMinutes(5) };
        var b = Snapshot("old-b") with { ExpiresAt = a.ExpiresAt };
        cache.Put(a).ShouldBeTrue();
        cache.Put(b).ShouldBeTrue();
        var offer = new HotelOffer(
            "semantic",
            "Test",
            "new-rate",
            ReservationStateTests.Terms(),
            null,
            true,
            null
        );
        var c = new HotelRatesSnapshot(
            Guid.NewGuid(),
            a.Id,
            new(a.Request, offer),
            new(ExecutionKind.Complete, [offer]),
            a.ExpiresAt
        );
        var d = Snapshot("new-search") with { ExpiresAt = a.ExpiresAt };
        cache.Put(c).ShouldBeTrue();
        cache.Put(d).ShouldBeTrue();
        var observed = JsonSerializer.Serialize(
            new
            {
                oldA = cache.Get(a.Id) is not null,
                oldB = cache.Get(b.Id) is not null,
                newRates = cache.GetRates(c.Id) is not null,
                newSearch = cache.Get(d.Id) is not null,
                count = cache.Count,
                bytes = cache.PayloadBytes,
            }
        );
        TestContext.Current.TestOutputHelper?.WriteLine(observed);
        cache.GetRates(c.Id).ShouldNotBeNull(observed);
        cache.Get(b.Id).ShouldBeNull(observed);
    }

    [Fact]
    public void Cache_full_predeclared_entry_and_byte_pressure_evicts_complete_old_snapshots()
    {
        var time = new FrozenCacheTime(DateTimeOffset.UtcNow);
        var cache = new HotelSnapshotCache(time);
        var terms = new StayTerms(
            new("acc_cache_fictional", "Fictional cache hotel", null, null, null),
            "Double",
            "rate_cache",
            new(
                DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(7),
                DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(9)
            ),
            new(1),
            new(120.25m, "GBP"),
            [],
            [],
            new("NonRefundable", []),
            [],
            true,
            "ImmediateTest"
        );
        var request = new StaySearch(HotelSearchService.Areas[0], terms.Dates, terms.Occupancy);
        void Fill(bool large)
        {
            for (var i = 0; i < 140; i++)
            {
                var offers = Enumerable
                    .Range(0, large ? 100 : 1)
                    .Select(j => new HotelOffer(
                        "duffel-stays",
                        "Test",
                        "cache_" + i + "_" + j,
                        terms with
                        {
                            Conditions = large
                                ? [new(new string('x', 2048), "Fictional source scope")]
                                : [],
                        },
                        null,
                        true,
                        null
                    ))
                    .ToArray();
                cache
                    .Put(
                        new HotelSearchSnapshot(
                            Guid.NewGuid(),
                            request,
                            new(ExecutionKind.Complete, offers),
                            [new("duffel-stays", ExecutionKind.Complete, null)],
                            time.GetUtcNow().AddMinutes(5)
                        )
                    )
                    .ShouldBeTrue();
            }
        }
        Fill(false);
        var entryCap = cache.Count;
        Fill(true);
        var byteCapEntries = cache.Count;
        var byteCapBytes = cache.PayloadBytes;
        Fill(false);
        var observed = JsonSerializer.Serialize(
            new
            {
                entryCap,
                byteCapEntries,
                byteCapBytes,
                finalEntries = cache.Count,
                finalBytes = cache.PayloadBytes,
            }
        );
        TestContext.Current.TestOutputHelper?.WriteLine(observed);
        entryCap.ShouldBe(128, observed);
        byteCapEntries.ShouldBeLessThan(128, observed);
        byteCapBytes.ShouldBeLessThanOrEqualTo(16777216, observed);
        cache.Count.ShouldBe(128, observed);
    }

    private sealed class FrozenCacheTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, true)]
    public async Task Two_small_semantic_sources_keep_offline_and_defensive_execution_support_aligned(
        int supportedAdults,
        int requestedAdults,
        bool supported
    )
    {
        var provider = new SemanticProvider(supportedAdults);
        var request = new StaySearch(
            HotelSearchService.Areas[3],
            ReservationStateTests.Terms().Dates,
            new(requestedAdults)
        );
        var check = provider.GetSupport(request);
        check.Supported.ShouldBe(supported);
        var result = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(supported ? ExecutionKind.Complete : ExecutionKind.Unsupported);
        result.Reason.ShouldBe(check.Reason);
        provider.Calls.ShouldBe(supported ? 1 : 0);
    }

    [Theory]
    [InlineData(ExecutionKind.Failed)]
    [InlineData(ExecutionKind.Limited)]
    public void Full_search_and_rates_snapshots_keep_status_reason_terms_scope_and_shared_cache_budget(
        ExecutionKind kind
    )
    {
        var terms = ReservationStateTests.Terms();
        var request = new StaySearch(HotelSearchService.Areas[0], terms.Dates, terms.Occupancy);
        var offer = new HotelOffer(
            "semantic",
            "Test",
            "selection_fictional",
            terms,
            DateTimeOffset.UtcNow.AddMinutes(4),
            true,
            null
        );
        var result = new HotelExecution<HotelOffer>(
            kind,
            kind == ExecutionKind.Limited ? [offer] : [],
            "FictionalReason"
        );
        var snapshot = new HotelSearchSnapshot(
            Guid.NewGuid(),
            request,
            result,
            [new("semantic", kind, "FictionalReason")],
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
        var cache = new HotelSnapshotCache(TimeProvider.System, 2, 32000);
        cache.Put(snapshot).ShouldBeTrue();
        var roundTrip = JsonSerializer.Deserialize<HotelSearchSnapshot>(
            JsonSerializer.Serialize(cache.Get(snapshot.Id))
        )!;
        JsonSerializer.Serialize(roundTrip).ShouldBe(JsonSerializer.Serialize(snapshot));
        var rates = new HotelRatesSnapshot(
            Guid.NewGuid(),
            snapshot.Id,
            new(request, offer),
            result,
            snapshot.ExpiresAt
        );
        cache.Put(rates).ShouldBeTrue();
        JsonSerializer
            .Serialize(
                JsonSerializer.Deserialize<HotelRatesSnapshot>(
                    JsonSerializer.Serialize(cache.GetRates(rates.Id))
                )
            )
            .ShouldBe(JsonSerializer.Serialize(rates));
        cache.Count.ShouldBe(2);
        cache.Put(Snapshot("third whole entry")).ShouldBeTrue();
        cache.Get(snapshot.Id).ShouldBeNull();
        cache.GetRates(rates.Id).ShouldNotBeNull();
        cache.Count.ShouldBe(2);
        var expired = rates with
        {
            Id = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        cache.Put(expired).ShouldBeFalse();
        cache.GetRates(expired.Id).ShouldBeNull();
    }

    [Fact]
    public async Task Unsupported_and_complete_empty_have_different_cached_meaning()
    {
        var time = TimeProvider.System;
        var provider = new SemanticProvider(1);
        var cache = new HotelSnapshotCache(time, 2, 16000);
        var service = new HotelSearchService([provider], cache, time);
        var request = new StaySearch(
            HotelSearchService.Areas.Single(a => a.Key == "fictional"),
            new(
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(4)
            ),
            new(2)
        );
        provider.GetSupport(request).Supported.ShouldBeFalse();
        var direct = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        var snapshot = (
            await service.SearchAsync(request, TestContext.Current.CancellationToken)
        ).Value;
        snapshot.Result.Kind.ShouldBe(direct.Kind);
        snapshot.Result.Reason.ShouldBe(direct.Reason);
        var restored = JsonSerializer.Deserialize<HotelSearchSnapshot>(
            JsonSerializer.Serialize(cache.Get(snapshot.Id)!)
        )!;
        restored.Result.Kind.ShouldBe(ExecutionKind.Unsupported);
        restored.Statuses.Single().Reason.ShouldBe("AdultsUnsupported");
        var empty = (
            await service.SearchAsync(
                request with
                {
                    Occupancy = new(1),
                },
                TestContext.Current.CancellationToken
            )
        ).Value;
        empty.Result.Kind.ShouldBe(ExecutionKind.Complete);
        empty.Result.Items.ShouldBeEmpty();
        provider.Calls.ShouldBe(1);
    }

    [Fact]
    public void Cache_accounts_whole_snapshots_and_rejects_oversized_entries()
    {
        var cache = new HotelSnapshotCache(TimeProvider.System, 1, 4000);
        var first = Snapshot("small");
        cache.Put(first).ShouldBeTrue();
        var second = Snapshot("other");
        cache.Put(second).ShouldBeTrue();
        cache.Get(first.Id).ShouldBeNull();
        cache.Get(second.Id)!.Result.Reason.ShouldBe("other");
        cache.Put(Snapshot(new string('x', 5000))).ShouldBeFalse();
        cache.Count.ShouldBe(1);
        cache.PayloadBytes.ShouldBeLessThanOrEqualTo(4000);
    }

    private static HotelSearchSnapshot Snapshot(string reason) =>
        new(
            Guid.NewGuid(),
            new(new("demo", 1, 1, 1), new(new(2027, 1, 1), new(2027, 1, 2)), new(1)),
            new(ExecutionKind.Limited, [], reason),
            [new("fake", ExecutionKind.Limited, reason)],
            DateTimeOffset.UtcNow.AddMinutes(5)
        );

    private sealed class SemanticProvider(int adults) : IHotelSearchProvider
    {
        public string Key => "semantic";
        public int Calls { get; private set; }

        public CapabilitySupport GetSupport(StaySearch request) =>
            request.Occupancy.Adults <= adults
                ? CapabilitySupport.Yes
                : CapabilitySupport.No("AdultsUnsupported");

        public Task<HotelExecution<HotelOffer>> SearchAsync(
            StaySearch request,
            CancellationToken cancellationToken
        )
        {
            var support = GetSupport(request);
            if (!support.Supported)
                return Task.FromResult(HotelExecution<HotelOffer>.Unsupported(support.Reason!));
            Calls++;
            return Task.FromResult(new HotelExecution<HotelOffer>(ExecutionKind.Complete, []));
        }

        public Task<HotelExecution<HotelOffer>> LoadRatesAsync(
            SelectedStay selection,
            CancellationToken cancellationToken
        ) => SearchAsync(selection.Request, cancellationToken);
    }
}
