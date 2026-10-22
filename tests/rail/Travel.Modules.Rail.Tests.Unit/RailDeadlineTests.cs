using System.Collections.Immutable;
using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Xunit;

namespace Travel.Modules.Rail.Tests.Unit;

public sealed class RailDeadlineTests
{
    private static int SharedPermits(RailSearchService search)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var admission = typeof(RailSearchService).GetField("admission", flags)!.GetValue(search)!;
        return (
            (SemaphoreSlim)
                typeof(RailReadAdmission).GetField("permits", flags)!.GetValue(admission)!
        ).CurrentCount;
    }

    [Fact]
    public async Task Already_completed_child_after_six_seconds_cannot_bypass_its_five_second_deadline()
    {
        var clock = new FakeTimeProvider(Start);
        var loader = new Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        var profile = new ReferenceServiceTests.Profile();
        using var references = new RailReferenceService(profile, loader, clock);
        var request = (await Request(references)) with { Transport = "train" };
        var provider = new Provider
        {
            Run = mode =>
            {
                clock.Advance(TimeSpan.FromSeconds(6));
                return Task.FromResult(
                    new ModeResult(mode, "complete", [], 0, [], clock.GetUtcNow())
                );
            },
        };
        var cache = new Cache();
        var search = new RailSearchService(profile, references, provider, cache, clock);
        var result = (
            await search.SearchAsync(request, TestContext.Current.CancellationToken)
        ).Value;
        result.Kind.ShouldBe("failed");
        result.Reasons.ShouldContain("timeout");
        cache.Writes.ShouldBe(0);
        provider.Calls.ShouldBe(1);
    }

    private sealed class FinalClock(FakeTimeProvider inner) : TimeProvider
    {
        public Action? OnRead;

        public override DateTimeOffset GetUtcNow()
        {
            OnRead?.Invoke();
            return inner.GetUtcNow();
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        ) => inner.CreateTimer(callback, state, dueTime, period);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    public async Task Finalization_cannot_publish_after_authority_changes(string change)
    {
        var fake = new FakeTimeProvider(Start);
        var clock = new FinalClock(fake);
        var profile = new ReferenceServiceTests.Profile();
        var loader = new Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        using var references = new RailReferenceService(profile, loader, clock);
        var request = await Request(references);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var cache = new Cache();
        var triggered = false;
        cache.Read = () =>
        {
            var reads = 0;
            clock.OnRead = () =>
            {
                if (++reads != 2)
                    return;
                triggered = true;
                clock.OnRead = null;
                if (change == "profile")
                    profile.Current = new("new", "fictional", false, default);
                if (change == "cancel")
                    caller.Cancel();
                if (change == "expiry")
                    fake.Advance(TimeSpan.FromMinutes(31));
            };
        };
        var search = new RailSearchService(profile, references, new Provider(), cache, clock);
        if (change == "cancel")
            await Should.ThrowAsync<OperationCanceledException>(async () =>
                await search.SearchAsync(request, caller.Token)
            );
        else
        {
            var result = await search.SearchAsync(request, caller.Token);
            if (change == "profile")
                result.Value.Kind.ShouldBe("skipped");
            else
                result.FirstError.Code.ShouldBe("Rail.StationSelection.Expired");
        }
        triggered.ShouldBeTrue();
        cache.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task Many_individually_bounded_fields_over_4MiB_fail_atomically_without_cache_write()
    {
        var clock = new FakeTimeProvider(Start);
        var profile = new ReferenceServiceTests.Profile();
        var loader = new Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        using var references = new RailReferenceService(profile, loader, clock);
        var request = await Request(references);
        var label = new string('я', 512);
        var ticket = new TicketInformation(
            "true",
            null,
            "reported",
            Enumerable
                .Range(0, 32)
                .Select(_ => new RailFare(
                    label,
                    new RailPrice("reported", "1234.05", "RUB", null, Start)
                ))
                .ToImmutableArray(),
            []
        );
        var provider = new Provider
        {
            Run = mode =>
                Task.FromResult(
                    new ModeResult(
                        mode,
                        "complete",
                        [],
                        50,
                        Enumerable
                            .Range(0, 50)
                            .Select(i => new ScheduledJourney(
                                mode + i,
                                mode + i,
                                mode,
                                request.From,
                                request.To,
                                Start.AddHours(10),
                                Start.AddHours(12),
                                7200,
                                null,
                                label,
                                label,
                                label,
                                label,
                                label,
                                label,
                                ticket,
                                Start,
                                []
                            ))
                            .ToImmutableArray(),
                        Start
                    )
                ),
        };
        var cache = new Cache();
        var search = new RailSearchService(profile, references, provider, cache, clock);
        var result = (
            await search.SearchAsync(request, TestContext.Current.CancellationToken)
        ).Value;
        result.Kind.ShouldBe("failed");
        result.Reasons.ShouldContain("normalization-limit");
        result.Items.ShouldBeEmpty();
        cache.Writes.ShouldBe(0);
        provider.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Cancelled_twenty_second_loader_accepts_no_new_subscribers_before_last_waiter_deadline()
    {
        var clock = new FakeTimeProvider(Start);
        var loader = new Loader();
        using var references = new RailReferenceService(
            new ReferenceServiceTests.Profile(),
            loader,
            clock
        );
        var original = references.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        await loader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(20));
        original.IsCompleted.ShouldBeFalse();
        var late = (
            await references.SuggestAsync("Стан", TestContext.Current.CancellationToken)
        ).Value;
        late.Kind.ShouldBe("failed");
        late.Reasons.ShouldContain("reference-timeout");
        loader.Calls.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(2));
        (await original).Value.Reasons.ShouldContain("reference-timeout");
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        await Settled(() =>
            typeof(RailReferenceService)
                .GetField(
                    "flight",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )!
                .GetValue(references)
                is null
        );
    }

    [Fact]
    public async Task Completion_cancellation_and_disposal_ownership_remain_bounded()
    {
        for (var i = 0; i < 100; i++)
        {
            var clock = new FakeTimeProvider(Start);
            var loader = new Loader();
            using var references = new RailReferenceService(
                new ReferenceServiceTests.Profile(),
                loader,
                clock
            );
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken
            );
            var pending = references.SuggestAsync("Стан", cancellation.Token);
            await loader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            var finish = Task.Run(
                () => loader.Completion.TrySetResult(ReferenceServiceTests.Data(Start)),
                TestContext.Current.CancellationToken
            );
            cancellation.Cancel();
            references.Dispose();
            try
            {
                await pending;
            }
            catch (OperationCanceledException) { }
            await finish;
            await Settled(() =>
                typeof(RailReferenceService)
                    .GetField(
                        "flight",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )!
                    .GetValue(references)
                    is null
            );
        }
    }

    private sealed class CooperativeLoader : IRailReferenceLoader
    {
        public TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public async Task<ErrorOr<ReferenceData>> LoadAsync(
            RailProfile profile,
            CancellationToken ct
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Error.Failure("unreachable");
        }
    }

    [Fact]
    public async Task Cooperative_twenty_second_deadline_retains_timeout_reason()
    {
        var clock = new FakeTimeProvider(Start);
        var cooperative = new CooperativeLoader();
        using var references = new RailReferenceService(
            new ReferenceServiceTests.Profile(),
            cooperative,
            clock
        );
        var pending = references.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        await cooperative.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(20));
        var result = (await pending).Value;
        result.Kind.ShouldBe("failed");
        result.Reasons.ShouldContain("reference-timeout");
    }

    [Fact]
    public async Task Reference_failure_after_disable_cannot_publish_old_profile()
    {
        var clock = new FakeTimeProvider(Start);
        var profile = new ReferenceServiceTests.Profile();
        var loader = new Loader();
        using var references = new RailReferenceService(profile, loader, clock);
        var pending = references.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        await loader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        profile.Current = new("new-epoch", "real", false, default);
        loader.Completion.SetResult(Error.Failure("transport"));
        var result = (await pending).Value;
        result.ProfileEpoch.ShouldBe("new-epoch");
        result.Kind.ShouldBe("skipped");
        result.Reasons.ShouldContain("profile-changed");
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    private sealed class Loader : IRailReferenceLoader
    {
        public TaskCompletionSource<ErrorOr<ReferenceData>> Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public int Calls;
        public CancellationToken Token;
        public TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<ErrorOr<ReferenceData>> LoadAsync(RailProfile profile, CancellationToken ct)
        {
            Calls++;
            Token = ct;
            Started.TrySetResult();
            return Completion.Task;
        }
    }

    private sealed class Cache : IRailSearchCache
    {
        public int Writes;
        public Action? Read;
        public RailSearchResult? Value;

        public RailSearchResult? Get(string key, DateTimeOffset now)
        {
            Read?.Invoke();
            return Value;
        }

        public void Set(string key, RailSearchResult value)
        {
            Writes++;
            Value = value;
        }

        public void Clear() => Value = null;
    }

    private sealed class Provider : IRailScheduleProvider
    {
        public int Calls;
        public Func<string, Task<ModeResult>> Run = mode =>
            Task.FromResult(new ModeResult(mode, "complete", [], 0, [], Start));

        public Task<ModeResult> SearchAsync(
            RailRequest request,
            string mode,
            RailProfile profile,
            CancellationToken ct
        )
        {
            Calls++;
            return Run(mode);
        }
    }

    private static async Task Settled(Func<bool> done)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!done())
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static async Task<RailRequest> Request(RailReferenceService references)
    {
        var result = (
            await references.SuggestAsync("Стан", TestContext.Current.CancellationToken)
        ).Value;
        return new(
            result.Suggestions[0].Reference,
            result.Suggestions[1].Reference,
            result.CatalogueGeneration!,
            "2026-10-20",
            "all"
        );
    }

    [Fact]
    public async Task One_waiter_cancellation_does_not_cancel_other_waiter()
    {
        var clock = new FakeTimeProvider(Start);
        var loader = new Loader();
        using var references = new RailReferenceService(
            new ReferenceServiceTests.Profile(),
            loader,
            clock
        );
        using var firstCancel = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var first = references.SuggestAsync("Стан", firstCancel.Token);
        var second = references.SuggestAsync("Город", TestContext.Current.CancellationToken);
        await loader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        firstCancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first);
        loader.Token.IsCancellationRequested.ShouldBeFalse();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        (await second).Value.Kind.ShouldBe("limited");
        loader.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Own_22_second_timeout_is_typed_and_retains_loader_then_cooldown()
    {
        var clock = new FakeTimeProvider(Start);
        var loader = new Loader();
        using var references = new RailReferenceService(
            new ReferenceServiceTests.Profile(),
            loader,
            clock
        );
        var pending = references.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        await loader.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(22));
        var result = await pending;
        result.Value.Kind.ShouldBe("failed");
        result.Value.Reasons.ShouldContain("reference-timeout");
        loader.Token.IsCancellationRequested.ShouldBeTrue();
        (
            await references.SuggestAsync("Стан", TestContext.Current.CancellationToken)
        ).Value.Reasons.ShouldContain("loading-cancelled");
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        await Settled(() =>
            typeof(RailReferenceService)
                .GetField(
                    "flight",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )!
                .GetValue(references)
                is null
        );
        (
            await references.SuggestAsync("Стан", TestContext.Current.CancellationToken)
        ).Value.Reasons.ShouldContain("refresh-cooldown");
        loader.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Five_second_child_timeout_does_not_release_uncooperative_work()
    {
        var clock = new FakeTimeProvider(Start);
        var loader = new Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        using var references = new RailReferenceService(
            new ReferenceServiceTests.Profile(),
            loader,
            clock
        );
        var request = await Request(references);
        var provider = new Provider();
        var held = new TaskCompletionSource<ModeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        provider.Run = _ => held.Task;
        var cache = new Cache();
        var search = new RailSearchService(
            new ReferenceServiceTests.Profile(),
            references,
            provider,
            cache,
            clock
        );
        var first = search.SearchAsync(request, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        (await first).Value.Kind.ShouldBe("failed");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var second = search.SearchAsync(request, cancel.Token);
        (
            await search.SearchAsync(request, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Rail.Capacity");
        provider.Calls.ShouldBe(4);
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await second);
        cache.Writes.ShouldBe(0);
        held.SetResult(new("train", "complete", [], 0, [], Start));
        await Settled(() => SharedPermits(search) == 2);
    }

    [Fact]
    public async Task Ten_seconds_of_bundle_life_clips_search_and_cache_hit_publication_rechecks_epoch()
    {
        var clock = new FakeTimeProvider(Start);
        var profile = new ReferenceServiceTests.Profile();
        var loader = new Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        using var references = new RailReferenceService(profile, loader, clock);
        var request = await Request(references);
        clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(10));
        var provider = new Provider
        {
            Run = mode =>
                Task.FromResult(new ModeResult(mode, "complete", [], 0, [], clock.GetUtcNow())),
        };
        var cache = new Cache();
        var search = new RailSearchService(profile, references, provider, cache, clock);
        var first = (
            await search.SearchAsync(request, TestContext.Current.CancellationToken)
        ).Value;
        first.ExpiresAt.ShouldBe(Start.AddMinutes(30));
        cache.Read = () => profile.Current = new("new-epoch", "fictional", false, default);
        var next = (await search.SearchAsync(request, TestContext.Current.CancellationToken)).Value;
        next.Kind.ShouldBe("skipped");
        next.Items.ShouldBeEmpty();
        provider.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Midflight_profile_revoke_prevents_old_result_and_cache_write()
    {
        var clock = new FakeTimeProvider(Start);
        using var revoked = new CancellationTokenSource();
        var profile = new ReferenceServiceTests.Profile
        {
            Current = new("epoch", "fictional", true, revoked.Token),
        };
        var loader = new Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(Start));
        using var references = new RailReferenceService(profile, loader, clock);
        var request = await Request(references);
        var held = new TaskCompletionSource<ModeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var provider = new Provider { Run = _ => held.Task };
        var cache = new Cache();
        var search = new RailSearchService(profile, references, provider, cache, clock);
        var pending = search.SearchAsync(request, TestContext.Current.CancellationToken);
        profile.Current = new("new", "fictional", false, default);
        revoked.Cancel();
        var result = (await pending).Value;
        result.Kind.ShouldBe("skipped");
        cache.Writes.ShouldBe(0);
        held.SetResult(new("train", "complete", [], 0, [], Start));
        await Settled(() => SharedPermits(search) == 2);
    }
}
