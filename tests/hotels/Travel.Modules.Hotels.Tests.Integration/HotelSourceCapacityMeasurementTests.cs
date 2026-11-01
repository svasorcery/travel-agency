using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

// Pure admission/counter regression: no Host, database, sockets or fixture lifecycle.
public sealed class HotelSourceCapacityMeasurementTests
{
    [Fact]
    public void Source_windows_and_releases_do_not_collapse_into_the_aggregate()
    {
        var began = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(began);
        var capacity = new HotelCapacity(Options.Create(new HotelsOptions()), clock);
        using var duffel = capacity.TryAcquire("duffel-stays");
        using var recovery = capacity.TryAcquire("duffel-stays", recovery: true);
        duffel.ShouldNotBeNull();
        recovery.ShouldNotBeNull();
        clock.Advance(TimeSpan.FromSeconds(10));
        using var hotelbeds = capacity.TryAcquire("hotelbeds");
        hotelbeds.ShouldNotBeNull();

        var before = Capture(capacity);
        var d = Source(before, "duffel-stays");
        var h = Source(before, "hotelbeds");
        d.GetProperty("capacity")
            .GetProperty("windowUntil")
            .GetDateTimeOffset()
            .ShouldBe(began.AddSeconds(60));
        h.GetProperty("capacity")
            .GetProperty("windowUntil")
            .GetDateTimeOffset()
            .ShouldBe(began.AddSeconds(70));
        d.GetProperty("capacity").GetProperty("totalStarts").GetInt32().ShouldBe(2);
        h.GetProperty("capacity").GetProperty("totalStarts").GetInt32().ShouldBe(1);
        d.GetProperty("capacity").GetProperty("maxActive").GetInt32().ShouldBe(2);
        h.GetProperty("capacity").GetProperty("maxActive").GetInt32().ShouldBe(1);
        capacity.GetSnapshot().MaxActive.ShouldBe(3);

        duffel.Dispose();
        duffel.Dispose();
        var released = Source(Capture(capacity), "duffel-stays");
        released.GetProperty("releases").GetInt32().ShouldBe(1);
        released.GetProperty("capacity").GetProperty("foregroundActive").GetInt32().ShouldBe(0);
        released.GetProperty("capacity").GetProperty("recoveryActive").GetInt32().ShouldBe(1);
        Source(Capture(capacity), "hotelbeds").GetProperty("releases").GetInt32().ShouldBe(0);
        d.GetProperty("capacity").GetProperty("foregroundActive").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Refused_admissions_and_unknown_sources_do_not_manufacture_starts_or_releases()
    {
        var clock = new FakeTimeProvider();
        var capacity = new HotelCapacity(Options.Create(new HotelsOptions()), clock);
        using var active = capacity.TryAcquire("hotelbeds");
        active.ShouldNotBeNull();
        capacity.TryAcquire("hotelbeds").ShouldBeNull();
        capacity.TryAcquire("unknown").ShouldBeNull();
        var snapshots = Capture(capacity);
        snapshots.GetArrayLength().ShouldBe(2);
        var h = Source(snapshots, "hotelbeds");
        h.GetProperty("capacity").GetProperty("totalStarts").GetInt32().ShouldBe(1);
        h.GetProperty("releases").GetInt32().ShouldBe(0);
        Source(snapshots, "duffel-stays")
            .GetProperty("capacity")
            .GetProperty("totalStarts")
            .GetInt32()
            .ShouldBe(0);
    }

    private static JsonElement Capture(HotelCapacity capacity) =>
        JsonSerializer.SerializeToElement(
            capacity.GetSourceSnapshots(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

    private static JsonElement Source(JsonElement values, string provider) =>
        values
            .EnumerateArray()
            .Single(value => value.GetProperty("provider").GetString() == provider);
}
