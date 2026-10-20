using System.Collections.Immutable;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class CacheTests
{
    private sealed class Profile : IRailRuntimeProfile
    {
        public RailProfile Current { get; set; } = new("epoch", "fictional", false, default);
    }

    [Fact]
    public void Disabled_profile_cannot_write_even_after_serialization()
    {
        var clock = new FakeTimeProvider();
        var now = clock.GetUtcNow();
        using var cache = new RailSearchCache(clock, new Profile());
        cache.Set("old", Value(now));
        cache.Get("old", now).ShouldBeNull();
    }

    [Fact]
    public void Absolute_expiry_evicts_without_a_cache_read_or_one_second_delay()
    {
        var clock = new FakeTimeProvider();
        var now = clock.GetUtcNow();
        using var cache = new RailSearchCache(clock);
        cache.Set("expiring", Value(now) with { ExpiresAt = now.AddMilliseconds(50) });
        clock.Advance(TimeSpan.FromMilliseconds(50));
        var entries = (System.Collections.IDictionary)
            typeof(RailSearchCache)
                .GetField(
                    "entries",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )!
                .GetValue(cache)!;
        entries.Count.ShouldBe(0);
    }

    private static RailSearchResult Value(DateTimeOffset now) =>
        new(
            "complete",
            [],
            ScheduleParserTests.Request,
            "yandex-rasp",
            "fictional",
            "epoch",
            [new ModeEvidence("train", "complete", [], 0, 0, now)],
            [],
            now,
            now.AddSeconds(30),
            now,
            new("/rail-fictional-banner.html", "fictional", "https://rasp.yandex.ru/")
        );

    [Fact]
    public void Cache_evicts_at_128_and_expiry_is_absolute()
    {
        using var cache = new RailSearchCache(TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 129; i++)
            cache.Set(i.ToString(System.Globalization.CultureInfo.InvariantCulture), Value(now));
        cache.Get("0", now).ShouldBeNull();
        cache.Get("128", now).ShouldNotBeNull();
        cache.Get("128", now.AddSeconds(31)).ShouldBeNull();
    }

    [Fact]
    public void Limited_and_oversized_entries_are_not_saved()
    {
        using var cache = new RailSearchCache(TimeProvider.System);
        var now = DateTimeOffset.UtcNow;
        cache.Set("limited", Value(now) with { Kind = "limited" });
        cache.Get("limited", now).ShouldBeNull();
        cache.Set(
            "big",
            Value(now) with
            {
                Reasons = ImmutableArray.Create(new string('x', 300000)),
            }
        );
        cache.Get("big", now).ShouldBeNull();
    }
}
