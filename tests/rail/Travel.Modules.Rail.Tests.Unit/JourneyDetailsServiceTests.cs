using System.Collections.Immutable;
using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Xunit;

namespace Travel.Modules.Rail.Tests.Unit;

public sealed class JourneyDetailsServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_invalid_operation_remains_sanitized_typed_failure_in_each_stage(
        bool routeStage
    )
    {
        var c = await Create();
        if (routeStage)
            c.Provider.ReadRoute = () =>
                throw new InvalidOperationException("provider-private-canary");
        else
            c.Provider.Verify = () =>
                throw new InvalidOperationException("provider-private-canary");
        var result = await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Kind.ShouldBe("failed");
        result.Value.Reasons.ShouldBe(["provider-failed"]);
        result.Value.Stage.ShouldBe(routeStage ? "route" : "verification");
        result.Value.Stops.ShouldBeEmpty();
        System
            .Text.Json.JsonSerializer.Serialize(result.Value)
            .ShouldNotContain("provider-private-canary");
    }

    [Fact]
    public async Task Limited_fresh_page_can_confirm_exact_match_and_conflicting_locator_duplicates_fail_closed()
    {
        var c = await Create();
        c.Provider.Page = c.Provider.Page with
        {
            Page = c.Provider.Page.Page with { Kind = "limited", Reasons = ["page-limit"] },
        };
        var reply = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        reply.Kind.ShouldBe("complete");
        reply.Verification!.Kind.ShouldBe("limited");
        c.Provider.Routes.ShouldBe(1);
        c = await Create();
        var fact = c.Provider.Page.Locators[0];
        c.Provider.Page = c.Provider.Page with
        {
            Locators =
            [
                fact,
                fact with
                {
                    Locator = fact.Locator! with { SourceLookupDate = "2026-10-18" },
                },
            ],
        };
        reply = (await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)).Value;
        reply.Reasons.ShouldContain("invalid-route-locator");
        c.Provider.Routes.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_wins_over_provider_exception_in_each_stage(
        bool routeStage
    )
    {
        var c = await Create();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        if (routeStage)
            c.Provider.ReadRoute = () =>
            {
                caller.Cancel();
                throw new IOException("private-source-canary");
            };
        else
            c.Provider.Verify = () =>
            {
                caller.Cancel();
                throw new IOException("private-source-canary");
            };
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await c.Service.GetAsync(c.Request, caller.Token)
        );
    }

    [Fact]
    public async Task Child_deadline_wins_over_synchronous_provider_exception()
    {
        var c = await Create();
        c.Provider.Verify = () =>
        {
            c.Clock.Advance(TimeSpan.FromSeconds(6));
            throw new IOException("private-source-canary");
        };
        var reply = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        reply.Reasons.ShouldContain("timeout");
        c.Provider.Routes.ShouldBe(0);
    }

    private sealed class Cache : IRailSearchCache
    {
        public RailSearchResult? Value;
        public int Removes;

        public RailSearchResult? Get(string key, DateTimeOffset now) => Value;

        public void Set(string key, RailSearchResult result) => Value = result;

        public void Clear() => throw new InvalidOperationException("Whole cache clear prohibited");

        public bool RemoveIfSame(string key, RailSearchResult captured)
        {
            if (!ReferenceEquals(Value, captured))
                return false;
            Removes++;
            Value = null;
            return true;
        }
    }

    private sealed class Provider
        : IRailJourneyVerificationProvider,
            IRailJourneyRouteProvider,
            IRailScheduleProvider
    {
        public int Verifications;
        public int Routes;
        public int Searches;
        public required RailVerificationPage Page;
        public required RailRouteResult Route;
        public Func<Task<RailVerificationPage>>? Verify;
        public Func<Task<RailRouteResult>>? ReadRoute;
        public Func<string, Task<ModeResult>>? Schedule;

        public Task<ModeResult> SearchAsync(
            RailRequest request,
            string mode,
            RailProfile profile,
            CancellationToken cancellationToken
        )
        {
            Searches++;
            return Schedule?.Invoke(mode) ?? Task.FromResult(Page.Page);
        }

        public Task<RailVerificationPage> VerifyAsync(
            RailRequest request,
            string mode,
            RailProfile profile,
            CancellationToken cancellationToken
        )
        {
            Verifications++;
            return Verify?.Invoke() ?? Task.FromResult(Page);
        }

        public Task<RailRouteResult> RouteAsync(
            ScheduledJourney journey,
            RailRouteLocator locator,
            RailProfile profile,
            CancellationToken cancellationToken
        )
        {
            Routes++;
            locator.SourceLookupDate.ShouldBe("2026-10-19");
            return ReadRoute?.Invoke() ?? Task.FromResult(Route);
        }
    }

    private sealed record Context(
        RailJourneyDetailsService Service,
        RailJourneyDetailsRequest Request,
        Provider Provider,
        ReferenceServiceTests.Profile Profile,
        FakeTimeProvider Clock,
        RailReadAdmission Admission,
        Cache Cache,
        RailSearchService Search
    );

    private static async Task<Context> Create()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        var profile = new ReferenceServiceTests.Profile
        {
            Current = new(new string('a', 32), "fictional", true, default, true),
        };
        var loader = new ReferenceServiceTests.Loader();
        loader.Completion.SetResult(ReferenceServiceTests.Data(clock.GetUtcNow()));
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
        var departure = clock.GetUtcNow().AddHours(10);
        var arrival = departure.AddHours(2);
        var selection = new RailJourneySelection("opaque-key", "train", departure, arrival);
        var journey = new ScheduledJourney(
            selection.Key,
            "uid",
            "train",
            request.From,
            request.To,
            departure,
            arrival,
            7200,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new("unknown", null, "not-provided", [], []),
            clock.GetUtcNow(),
            []
        );
        var stops = ImmutableArray.Create(
            new RailRouteStop(
                0,
                new("yandex-rasp", request.From.Code, "A"),
                RailStopFact<DateTimeOffset>.Missing("terminal-not-provided"),
                RailStopFact<DateTimeOffset>.Reported(departure),
                RailStopFact<long>.Missing("missing-dwell"),
                RailTextFact.Missing("missing-platform"),
                []
            ),
            new RailRouteStop(
                1,
                new("yandex-rasp", request.To.Code, "B"),
                RailStopFact<DateTimeOffset>.Reported(arrival),
                RailStopFact<DateTimeOffset>.Missing("terminal-not-provided"),
                RailStopFact<long>.Missing("missing-dwell"),
                RailTextFact.Missing("missing-platform"),
                []
            )
        );
        var provider = new Provider
        {
            Page = new(
                new("train", "complete", [], 1, [journey], clock.GetUtcNow()),
                [new(selection.Key, "reported", new("yandex-rasp", "uid", "2026-10-19"))]
            ),
            Route = new("complete", [], stops, 0, 1, clock.GetUtcNow()),
        };
        var cache = new Cache();
        var admission = new RailReadAdmission();
        return new(
            new(profile, references, provider, provider, cache, clock, admission),
            new(request, profile.Current.Epoch, selection),
            provider,
            profile,
            clock,
            admission,
            cache,
            new(profile, references, provider, cache, clock, admission)
        );
    }

    [Fact]
    public async Task Exact_fresh_match_uses_two_reads_without_returning_verification_prices()
    {
        var c = await Create();
        var result = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        result.Kind.ShouldBe("complete");
        result.Stops.Length.ShouldBe(2);
        result.ExpiresAt.ShouldBe(c.Clock.GetUtcNow().AddSeconds(60));
        c.Provider.Verifications.ShouldBe(1);
        c.Provider.Routes.ShouldBe(1);
        result.Verification!.Transport.ShouldBe("train");
        c.Cache.Removes.ShouldBe(0);
    }

    [Theory]
    [InlineData("rotated", "skipped", "selection-not-confirmed")]
    [InlineData("missing", "skipped", "route-locator-unavailable")]
    [InlineData("invalid", "failed", "invalid-route-locator")]
    [InlineData("failed", "failed", "transport")]
    public async Task Unconfirmed_selection_or_locator_never_dispatches_thread(
        string defect,
        string kind,
        string reason
    )
    {
        var c = await Create();
        c.Provider.Page = defect switch
        {
            "rotated" => c.Provider.Page with
            {
                Page = c.Provider.Page.Page with
                {
                    Items = [c.Provider.Page.Page.Items[0] with { Key = "new-uid" }],
                },
            },
            "failed" => new(ModeResult.Unavailable("train", "failed", "transport"), []),
            _ => c.Provider.Page with { Locators = [new(c.Request.Selection.Key, defect, null)] },
        };
        var result = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        result.Kind.ShouldBe(kind);
        result.Reasons.ShouldContain(reason);
        result.Stage.ShouldBe("verification");
        result.Stops.ShouldBeEmpty();
        c.Provider.Routes.ShouldBe(0);
    }

    [Fact]
    public async Task Default_off_and_profile_notice_make_no_reads_and_remove_all_evidence()
    {
        var c = await Create();
        c.Profile.Current = c.Profile.Current with { JourneyDetailsEnabled = false };
        var result = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        result.Reasons.ShouldContain("details-disabled");
        c.Provider.Verifications.ShouldBe(0);
        c.Profile.Current = c.Profile.Current with { Epoch = new string('b', 32) };
        result = (await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)).Value;
        result.Reasons.ShouldContain("profile-changed");
        result.Stage.ShouldBe("admission");
        result.Verification.ShouldBeNull();
        result.RouteReceivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelled_uncooperative_verification_holds_shared_permit_until_settled()
    {
        var c = await Create();
        var pending = new TaskCompletionSource<RailVerificationPage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        c.Provider.Verify = () => pending.Task;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var first = c.Service.GetAsync(c.Request, caller.Token);
        caller.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first);
        c.Admission.TryEnter().ShouldBeTrue();
        c.Admission.TryEnter().ShouldBeFalse();
        pending.SetResult(c.Provider.Page);
        await pending.Task;
        c.Admission.Release();
        await Free(c.Admission);
        c.Provider.Routes.ShouldBe(0);
    }

    [Fact]
    public async Task Whole_child_timeout_stops_second_read_and_profile_change_drops_old_provenance()
    {
        var c = await Create();
        c.Provider.Verify = () =>
        {
            c.Clock.Advance(TimeSpan.FromSeconds(6));
            return Task.FromResult(c.Provider.Page);
        };
        var result = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        result.Reasons.ShouldContain("timeout");
        c.Provider.Routes.ShouldBe(0);
        c = await Create();
        c.Provider.Verify = () =>
        {
            c.Profile.Current = c.Profile.Current with { Epoch = new string('b', 32) };
            return Task.FromResult(c.Provider.Page);
        };
        result = (await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)).Value;
        result.Stage.ShouldBe("admission");
        result.Verification.ShouldBeNull();
        c.Provider.Routes.ShouldBe(0);
    }

    private static async Task Free(RailReadAdmission admission)
    {
        for (var i = 0; i < 1000; i++)
        {
            if (admission.TryEnter())
            {
                var second = admission.TryEnter();
                admission.Release();
                if (second)
                {
                    admission.Release();
                    return;
                }
            }
            await Task.Yield();
        }
        throw new InvalidOperationException("Shared permits did not settle");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task All_search_detail_mixes_share_two_permits_through_actual_child_settlement(
        int detailCount
    )
    {
        var c = await Create();
        var verification = new TaskCompletionSource<RailVerificationPage>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var schedule = new TaskCompletionSource<ModeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        c.Provider.Verify = () => verification.Task;
        c.Provider.Schedule = _ => schedule.Task;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var operations = Enumerable
            .Range(0, 2)
            .Select(i =>
                i < detailCount
                    ? (Task)c.Service.GetAsync(c.Request, caller.Token)
                    : c.Search.SearchAsync(c.Request.Search, caller.Token)
            )
            .ToArray();
        (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Rail.Capacity");
        (
            await c.Search.SearchAsync(c.Request.Search, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Rail.Capacity");
        caller.Cancel();
        foreach (var operation in operations)
            await Should.ThrowAsync<OperationCanceledException>(async () => await operation);
        c.Admission.TryEnter().ShouldBeFalse();
        verification.SetResult(c.Provider.Page);
        schedule.SetResult(c.Provider.Page.Page);
        await Free(c.Admission);
        c.Provider.Routes.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Usable_no_match_evicts_only_old_entry_and_preserves_a_concurrent_refresh(
        bool concurrent
    )
    {
        var c = await Create();
        var now = c.Clock.GetUtcNow();
        var old = new RailSearchResult(
            "complete",
            [],
            c.Request.Search,
            "yandex-rasp",
            "fictional",
            c.Profile.Current.Epoch,
            [],
            c.Provider.Page.Page.Items,
            now,
            now.AddMinutes(5),
            now,
            null
        );
        c.Cache.Value = old;
        c.Provider.Verify = () =>
        {
            if (concurrent)
                c.Cache.Value = old with { CompletedAt = now.AddSeconds(1) };
            return Task.FromResult(
                c.Provider.Page with
                {
                    Page = c.Provider.Page.Page with
                    {
                        Kind = "limited",
                        Reasons = ["page-limit"],
                        Items = [],
                    },
                }
            );
        };
        var reply = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        reply.Reasons.ShouldContain("selection-not-confirmed");
        c.Cache.Removes.ShouldBe(concurrent ? 0 : 1);
        c.Provider.Routes.ShouldBe(0);
        old.Items.ShouldHaveSingleItem();
        await c.Search.SearchAsync(c.Request.Search, TestContext.Current.CancellationToken);
        c.Provider.Searches.ShouldBe(concurrent ? 0 : 2);
    }

    [Theory]
    [InlineData("verification")]
    [InlineData("locator")]
    [InlineData("route")]
    public async Task Source_or_locator_failure_retains_the_successful_search_snapshot(string stage)
    {
        var c = await Create();
        var now = c.Clock.GetUtcNow();
        var old = new RailSearchResult(
            "complete",
            [],
            c.Request.Search,
            "yandex-rasp",
            "fictional",
            c.Profile.Current.Epoch,
            [],
            c.Provider.Page.Page.Items,
            now,
            now.AddMinutes(5),
            now,
            null
        );
        c.Cache.Value = old;
        if (stage == "verification")
            c.Provider.Page = new(ModeResult.Unavailable("train", "skipped", "quota"), []);
        if (stage == "locator")
            c.Provider.Page = c.Provider.Page with
            {
                Locators = [new(c.Request.Selection.Key, "invalid", null)],
            };
        if (stage == "route")
            c.Provider.Route = RailRouteResult.Unavailable("failed", "transport");
        (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value.Stops.ShouldBeEmpty();
        c.Cache.Value.ShouldBeSameAs(old);
        c.Cache.Removes.ShouldBe(0);
    }

    [Fact]
    public async Task Expiry_between_stages_and_route_revocation_block_second_read_and_old_evidence()
    {
        var c = await Create();
        c.Provider.Verify = () =>
        {
            c.Clock.Advance(TimeSpan.FromMinutes(31));
            return Task.FromResult(c.Provider.Page);
        };
        (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Rail.StationSelection.Expired");
        c.Provider.Routes.ShouldBe(0);
        c = await Create();
        c.Provider.ReadRoute = () =>
        {
            c.Profile.Current = c.Profile.Current with
            {
                Epoch = new string('b', 32),
                JourneyDetailsEnabled = false,
            };
            return Task.FromResult(c.Provider.Route);
        };
        var reply = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        reply.Reasons.ShouldContain("profile-changed");
        reply.Verification.ShouldBeNull();
        reply.RouteReceivedAt.ShouldBeNull();
        reply.Attribution.ShouldBeNull();
    }

    [Fact]
    public async Task Late_fault_after_route_timeout_is_observed_and_lease_released_only_after_settlement()
    {
        var c = await Create();
        var route = new TaskCompletionSource<RailRouteResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        c.Provider.ReadRoute = () => route.Task;
        var pending = c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken);
        c.Clock.Advance(TimeSpan.FromSeconds(5));
        var reply = (await pending).Value;
        reply.Stage.ShouldBe("route");
        reply.Reasons.ShouldContain("timeout");
        reply.Stops.ShouldBeEmpty();
        reply.RouteReceivedAt.ShouldBeNull();
        c.Admission.TryEnter().ShouldBeTrue();
        c.Admission.TryEnter().ShouldBeFalse();
        c.Admission.Release();
        route.SetException(new InvalidOperationException("private-canary"));
        await Free(c.Admission);
    }

    [Fact]
    public async Task Normalized_route_over_512KiB_fails_without_truncation_or_search_mutation()
    {
        var c = await Create();
        var first = c.Provider.Route.Stops[0];
        var last = c.Provider.Route.Stops[1];
        var label = new string('я', 512);
        var middle = Enumerable
            .Range(1, 198)
            .Select(i =>
                first with
                {
                    Sequence = i,
                    Station = new("yandex-rasp", "s9", label),
                    Arrival = first.Departure,
                    Platform = RailTextFact.Reported(label),
                }
            );
        c.Provider.Route = c.Provider.Route with
        {
            Stops = ImmutableArray.Create(first).AddRange(middle).Add(last with { Sequence = 199 }),
            AlightingIndex = 199,
        };
        var reply = (
            await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken)
        ).Value;
        reply.Kind.ShouldBe("failed");
        reply.Reasons.ShouldContain("output-limit");
        reply.Stops.ShouldBeEmpty();
        c.Cache.Removes.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_reference_wins_over_provider_exception_in_each_stage(bool routeStage)
    {
        var c = await Create();
        if (routeStage)
            c.Provider.ReadRoute = () =>
            {
                c.Clock.Advance(TimeSpan.FromMinutes(31));
                throw new IOException("private-source-canary");
            };
        else
            c.Provider.Verify = () =>
            {
                c.Clock.Advance(TimeSpan.FromMinutes(31));
                throw new IOException("private-source-canary");
            };
        var result = await c.Service.GetAsync(c.Request, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Rail.StationSelection.Expired");
    }
}
