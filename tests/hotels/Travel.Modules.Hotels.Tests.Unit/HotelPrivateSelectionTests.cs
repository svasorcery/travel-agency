using System.Text.Json;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelPrivateSelectionTests
{
    [Fact]
    public void Mutating_returned_arrays_cannot_rewrite_the_cached_snapshot()
    {
        var terms = ReservationStateTests.Terms() with { Conditions = [new("Saved", "WholeStay")] };
        var offer = new HotelOffer("hotelbeds", "Test", "local", terms, null, true, null)
        {
            SelectionContext = new(2, "owned-private"),
        };
        var snapshot = new HotelSearchSnapshot(
            Guid.NewGuid(),
            new(HotelSearchService.Areas[3], terms.Dates, terms.Occupancy),
            new(ExecutionKind.Complete, [offer]),
            [],
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
        var cache = new HotelSnapshotCache(TimeProvider.System);
        cache.Put(snapshot).ShouldBeTrue();
        terms.Conditions[0] = new("Changed", "WholeStay");
        cache.Get(snapshot.Id)!.Result.Items[0].Terms.Conditions[0].Text.ShouldBe("Saved");
        cache.Get(snapshot.Id)!.Result.Items[0].Terms.Conditions[0] = new(
            "ChangedAgain",
            "WholeStay"
        );
        cache.Get(snapshot.Id)!.Result.Items[0].Terms.Conditions[0].Text.ShouldBe("Saved");
        cache.Get(snapshot.Id)!.Result.Items[0].SelectionContext!.Value.ShouldBe("owned-private");
    }

    [Fact]
    public void Private_handle_is_counted_in_same_entry_but_never_serialized()
    {
        var terms = ReservationStateTests.Terms();
        var secret = new string('x', 4096);
        var offer = new HotelOffer("hotelbeds", "Test", "local-token", terms, null, true, null)
        {
            SelectionContext = new(2, secret),
        };
        var snapshot = new HotelSearchSnapshot(
            Guid.NewGuid(),
            new(HotelSearchService.Areas[3], terms.Dates, terms.Occupancy),
            new(ExecutionKind.Complete, [offer]),
            [new("hotelbeds", ExecutionKind.Complete, null)],
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
        var cache = new HotelSnapshotCache(TimeProvider.System, 1, 32000);
        cache.Put(snapshot).ShouldBeTrue();
        JsonSerializer.Serialize(cache.Get(snapshot.Id)).ShouldNotContain(secret);
        cache.PayloadBytes.ShouldBeGreaterThan(secret.Length);
        cache.Get(snapshot.Id)!.Result.Items[0].SelectionContext.ShouldBe(offer.SelectionContext);
        cache.Put(snapshot with { Id = Guid.NewGuid() }).ShouldBeTrue();
        cache.Get(snapshot.Id).ShouldBeNull();
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void Oversized_private_entry_is_refused_without_truncating_handle()
    {
        var terms = ReservationStateTests.Terms();
        var offer = new HotelOffer("hotelbeds", "Test", "local", terms, null, true, null)
        {
            SelectionContext = new(2, new string('x', 4097)),
        };
        var snapshot = new HotelSearchSnapshot(
            Guid.NewGuid(),
            new(HotelSearchService.Areas[3], terms.Dates, terms.Occupancy),
            new(ExecutionKind.Complete, [offer]),
            [],
            DateTimeOffset.UtcNow.AddMinutes(5)
        );
        var cache = new HotelSnapshotCache(TimeProvider.System);
        cache.Put(snapshot).ShouldBeFalse();
        cache.Count.ShouldBe(0);
    }
}
