using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class SavedOptionPreviewReadinessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(10);
    private const string Brief =
        """{"brief":{"originId":"moscow","destinationId":"saint-petersburg","startDate":"2026-10-28","endDate":"2026-10-30","adults":2,"currency":"RUB","totalBudget":null,"interests":["architecture"],"pace":"relaxed"}}""";

    [Fact]
    public async Task Ready_storage_issues_one_selection_without_writing()
    {
        using var keys = new TripsTestRing();
        var probe = new Probe();
        probe.Release.SetResult(true);
        await using var fixture = await Start(probe, keys);
        using var response = await Select(fixture, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var card = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        card.RootElement.GetProperty("preview")
            .GetProperty("outcome")
            .GetString()
            .ShouldBe("option");
        card.RootElement.GetProperty("selection")
            .GetProperty("selectionToken")
            .GetString()
            .ShouldNotBeNullOrEmpty();
        probe.Checks.ShouldBe(1);
        probe.Issues.ShouldBe(1);
        probe.StoreCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stalled_readiness_returns_the_card_before_the_provider_finishes(
        bool succeedsLate
    )
    {
        using var keys = new TripsTestRing();
        using var releaseCallback = new ManualResetEventSlim();
        var probe = new Probe(releaseCallback);
        await using var fixture = await Start(probe, keys);
        var pending = Select(fixture, Ct);
        try
        {
            await probe.Entered.Task.WaitAsync(Watchdog, Ct);
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(3), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            using var card = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
            card.RootElement.GetProperty("preview")
                .GetProperty("outcome")
                .GetString()
                .ShouldBe("option");
            card.RootElement.GetProperty("selection").ValueKind.ShouldBe(JsonValueKind.Null);
            await probe.Cancelled.Task.WaitAsync(Watchdog, Ct);
            await probe.Effects.Completion("readiness", Ct);
            probe.Disposed.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            if (succeedsLate)
                probe.Release.TrySetResult(true);
            else
                probe.Release.TrySetException(new IOException("Fictional late database failure."));
            releaseCallback.Set();
        }
        await probe.Disposed.Task.WaitAsync(Watchdog, Ct);
        probe.Checks.ShouldBe(1);
        probe.Issues.ShouldBe(0);
        probe.StoreCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Aborted_client_cancels_the_check_and_exits_the_pipeline_without_issuing()
    {
        using var keys = new TripsTestRing();
        var probe = new Probe();
        await using var fixture = await Start(probe, keys);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pending = Select(fixture, abort.Token);
        try
        {
            await probe.Entered.Task.WaitAsync(Watchdog, Ct);
            await abort.CancelAsync().WaitAsync(Watchdog, Ct);
            await Should.ThrowAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(Watchdog, Ct)
            );
            await probe.Cancelled.Task.WaitAsync(Watchdog, Ct);
            var server = await probe.Effects.Completion("readiness", Ct);
            server.RequestAborted.ShouldBeTrue();
        }
        finally
        {
            probe.Release.TrySetResult(true);
        }
        await probe.Disposed.Task.WaitAsync(Watchdog, Ct);
        probe.Checks.ShouldBe(1);
        probe.Issues.ShouldBe(0);
        probe.StoreCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Unfinished_checks_cannot_accumulate_while_more_previews_arrive()
    {
        using var keys = new TripsTestRing();
        var probe = new Probe();
        await using var fixture = await Start(probe, keys);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                using var response = await Select(fixture, Ct).WaitAsync(Watchdog, Ct);
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                using var card = JsonDocument.Parse(
                    await response.Content.ReadAsByteArrayAsync(Ct)
                );
                card.RootElement.GetProperty("selection").ValueKind.ShouldBe(JsonValueKind.Null);
            }
            probe.Checks.ShouldBe(2);
            probe.Issues.ShouldBe(0);
            probe.StoreCalls.ShouldBe(0);
        }
        finally
        {
            probe.Release.TrySetResult(true);
        }
    }

    private static Task<TripsHttpFixture> Start(Probe probe, TripsTestRing keys) =>
        TripsHttpFixture.Start(
            itinerary: true,
            environment: "Testing",
            optionPreview: true,
            optionProfile: "spb-option",
            optionCatalogue: "B",
            saveOptions: true,
            configure: services =>
            {
                services.AddScoped<ITripsReadiness>(_ => new ScopedReadiness(probe));
                services.AddSingleton<ISavedOptionProtection>(_ =>
                {
                    var protection = new SavedOptionProtection(keys.Provider);
                    return TripOptionEffectsProxy.Observe<ISavedOptionProtection>(
                        (method, args) =>
                        {
                            Interlocked.Increment(ref probe.Issues);
                            return method.Invoke(protection, args);
                        }
                    );
                });
                services.AddSingleton(
                    TripOptionEffectsProxy.Observe<ISavedOptionStore>(
                        (_, _) =>
                        {
                            Interlocked.Increment(ref probe.StoreCalls);
                            throw new InvalidOperationException(
                                "A preview must not access the saved option store."
                            );
                        }
                    )
                );
            },
            configureAfterWolverine: probe.Effects.Register
        );

    private static async Task<HttpResponseMessage> Select(
        TripsHttpFixture fixture,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/trips/option-preview")
        {
            Content = new StringContent(Brief, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Trips-Option-Save", "1");
        request.Headers.Add(TripOptionEffectsProbe.Header, "readiness");
        return await fixture.Client.SendAsync(request, ct);
    }

    private sealed class Probe(ManualResetEventSlim? callbackGate = null)
    {
        internal readonly ManualResetEventSlim? CallbackGate = callbackGate;
        internal readonly TripOptionEffectsProbe Effects = new();
        internal readonly TaskCompletionSource Entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal readonly TaskCompletionSource Cancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal readonly TaskCompletionSource<bool> Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal readonly TaskCompletionSource Disposed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal int Checks;
        internal int Issues;
        internal int StoreCalls;
    }

    private sealed class ScopedReadiness(Probe probe) : ITripsReadiness, IDisposable
    {
        public async Task<bool> IsReadyAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref probe.Checks);
            using var registration = ct.Register(() =>
            {
                probe.Cancelled.TrySetResult();
                probe.CallbackGate?.Wait(Watchdog);
            });
            probe.Entered.TrySetResult();
            return await probe.Release.Task;
        }

        public void Dispose() => probe.Disposed.TrySetResult();
    }
}
