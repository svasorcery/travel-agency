using ErrorOr;
using Shouldly;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Xunit;

namespace Travel.Modules.Rail.Tests.Unit;

public sealed class SearchServiceTests
{
    private sealed class Cache : IRailSearchCache
    {
        public int Writes;
        private readonly Dictionary<string, RailSearchResult> values = [];

        public RailSearchResult? Get(string key, DateTimeOffset now) =>
            values.TryGetValue(key, out var result) && result.ExpiresAt > now ? result : null;

        public void Set(string key, RailSearchResult result)
        {
            Writes++;
            values[key] = result;
        }

        public void Clear() => values.Clear();
    }

    private sealed class Provider : IRailScheduleProvider
    {
        public int Calls;
        public Func<string, Task<ModeResult>> Run = mode =>
            Task.FromResult(
                new ModeResult(
                    mode,
                    "complete",
                    [],
                    0,
                    [],
                    new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)
                )
            );

        public Task<ModeResult> SearchAsync(
            RailRequest request,
            string mode,
            RailProfile profile,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Run(mode);
        }
    }

    private static async Task<(
        RailSearchService Service,
        RailRequest Request,
        ReferenceServiceTests.Clock Clock,
        Cache Cache,
        Provider Provider
    )> Create()
    {
        var clock = new ReferenceServiceTests.Clock();
        var profile = new ReferenceServiceTests.Profile();
        var loader = new ReferenceServiceTests.Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(clock.Now));
        var references = new RailReferenceService(profile, loader, clock);
        var suggestions = (
            await references.SuggestAsync("Стан", TestContext.Current.CancellationToken)
        ).Value;
        var request = new RailRequest(
            suggestions.Suggestions[0].Reference,
            suggestions.Suggestions[1].Reference,
            suggestions.CatalogueGeneration!,
            "2026-10-20",
            "all"
        );
        var cache = new Cache();
        var provider = new Provider();
        return (
            new RailSearchService(profile, references, provider, cache, clock),
            request,
            clock,
            cache,
            provider
        );
    }

    [Fact]
    public async Task Successful_empty_plus_failed_is_limited_zero_and_not_cached()
    {
        var c = await Create();
        c.Provider.Run = mode =>
            Task.FromResult(
                mode == "train"
                    ? new ModeResult(mode, "complete", [], 0, [], c.Clock.Now)
                    : ModeResult.Unavailable(mode, "failed", "timeout")
            );
        var result = (
            await c.Service.SearchAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        result.Kind.ShouldBe("limited");
        result.Items.ShouldBeEmpty();
        result.Modes.Length.ShouldBe(2);
        c.Provider.Calls.ShouldBe(2);
        c.Cache.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task Complete_empty_cache_keeps_original_time_and_short_expiry()
    {
        var c = await Create();
        var first = (
            await c.Service.SearchAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        c.Clock.Now = c.Clock.Now.AddSeconds(5);
        var hit = (
            await c.Service.SearchAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        hit.ShouldBe(first);
        hit.ExpiresAt.ShouldBe(first.ReceivedAt!.Value.AddSeconds(30));
        c.Provider.Calls.ShouldBe(2);
        c.Cache.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelled_uncooperative_children_hold_search_permits_until_settled()
    {
        var c = await Create();
        var pending = new TaskCompletionSource<ModeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        c.Provider.Run = _ => pending.Task;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var first = c.Service.SearchAsync(c.Request, cancel.Token);
        var second = c.Service.SearchAsync(c.Request, cancel.Token);
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first);
        await Should.ThrowAsync<OperationCanceledException>(async () => await second);
        (
            await c.Service.SearchAsync(c.Request, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Rail.Capacity");
        c.Provider.Calls.ShouldBe(4);
        c.Cache.Writes.ShouldBe(0);
        pending.SetResult(new ModeResult("train", "complete", [], 0, [], c.Clock.Now));
    }
}
