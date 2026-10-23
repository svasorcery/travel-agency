using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class RuntimeTransportTests
{
    [Fact]
    public async Task Turning_off_only_details_revokes_epoch_and_keeps_M1_enabled_without_thread_dispatch()
    {
        var clock = new Clock();
        var monitor = new Monitor(
            new()
            {
                Enabled = true,
                Fictional = true,
                JourneyDetailsEnabled = true,
            }
        );
        using var runtime = new RailRuntime(monitor, clock);
        var old = runtime.Current;
        monitor.Set(
            new()
            {
                Enabled = true,
                Fictional = true,
                JourneyDetailsEnabled = false,
            }
        );
        runtime.Current.Enabled.ShouldBeTrue();
        runtime.Current.JourneyDetailsEnabled.ShouldBeFalse();
        runtime.Current.Epoch.ShouldNotBe(old.Epoch);
        old.Revoked.IsCancellationRequested.ShouldBeTrue();
        var terminal = new Terminal();
        var client = new YandexRailClient(new HttpClient(terminal), runtime, new(clock), clock);
        (
            await client.GetAsync(
                "thread",
                "&uid=fictional&date=2026-10-20",
                runtime.Current,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("details-disabled");
        terminal.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Malformed_configured_header_is_safe_before_dispatch_and_quota_debit()
    {
        var clock = new Clock();
        var options = new RailOptions
        {
            Enabled = true,
            ApiKey = ":invalid-fictional-key",
            RightsConfirmed = true,
            BudgetConfirmed = true,
            RestartAllowanceConfirmed = true,
            MinuteAllowance = 1,
            DayAllowance = 1,
        };
        var monitor = new Monitor(options);
        using var runtime = new RailRuntime(monitor, clock);
        clock.Now = clock.Now.AddSeconds(1);
        options.BudgetConfirmedAt = clock.Now;
        monitor.Set(options);
        runtime.Current.Enabled.ShouldBeTrue();
        var terminal = new Terminal();
        var ledger = new RailQuotaLedger(clock);
        var client = new YandexRailClient(new HttpClient(terminal), runtime, ledger, clock);
        var result = await client.GetAsync(
            "copyright",
            "",
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("source-configuration");
        terminal.Calls.ShouldBe(0);
        ledger.TryDebit(1, 1).ShouldBeTrue();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Monitor(RailOptions value) : IOptionsMonitor<RailOptions>
    {
        private Action<RailOptions, string?>? changed;
        public RailOptions CurrentValue { get; private set; } = value;

        public RailOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<RailOptions, string?> listener)
        {
            changed += listener;
            return null;
        }

        public void Set(RailOptions options)
        {
            CurrentValue = options;
            changed?.Invoke(options, null);
        }
    }

    private sealed class Terminal : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.RequestUri!.Scheme.ShouldBe("https");
            request.RequestUri.Host.ShouldBe("api.rasp.yandex-net.ru");
            request.RequestUri.Query.ShouldNotContain("key");
            request.Headers.GetValues("Authorization").ShouldBe(new[] { "fictional-no-key" });
            Calls++;
            var response = new HttpResponseMessage(Status) { Content = new StringContent("{}") };
            if (Status == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(120)
                );
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void Real_requires_fresh_restart_budget_and_process_owned_epoch()
    {
        var monitor = new Monitor(new RailOptions());
        var runtime = new RailRuntime(monitor);
        runtime.Current.Enabled.ShouldBeFalse();
        var old = runtime.Current;
        monitor.Set(new RailOptions { Enabled = true, Fictional = true });
        runtime.Current.Enabled.ShouldBeTrue();
        runtime.Current.Epoch.ShouldNotBe(old.Epoch);
        old.Revoked.IsCancellationRequested.ShouldBeTrue();
        monitor.Set(
            new RailOptions
            {
                Enabled = true,
                ApiKey = "fictional",
                RightsConfirmed = true,
                BudgetConfirmed = true,
                MinuteAllowance = 2,
                DayAllowance = 10,
            }
        );
        runtime.Current.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Quota_is_atomic_nonrefundable_and_rolling()
    {
        var clock = new Clock();
        var quota = new RailQuotaLedger(clock);
        quota.TryDebit(1, 2).ShouldBeTrue();
        quota.TryDebit(1, 2).ShouldBeFalse();
        clock.Now = clock.Now.AddSeconds(61);
        quota.TryDebit(1, 2).ShouldBeTrue();
        clock.Now = clock.Now.AddSeconds(61);
        quota.TryDebit(1, 2).ShouldBeFalse();
        clock.Now = clock.Now.AddDays(1);
        quota.TryDebit(1, 2).ShouldBeTrue();
    }

    [Fact]
    public void Saved_real_confirmation_cannot_replay_after_restart()
    {
        var clock = new Clock();
        var options = new RailOptions
        {
            Enabled = true,
            ApiKey = "fictional-secret",
            RightsConfirmed = true,
            BudgetConfirmed = true,
            RestartAllowanceConfirmed = true,
            MinuteAllowance = 2,
            DayAllowance = 5,
            BudgetConfirmedAt = clock.Now.AddMinutes(-1),
        };
        var monitor = new Monitor(options);
        using var first = new RailRuntime(monitor, clock);
        first.Current.Enabled.ShouldBeFalse();
        clock.Now = clock.Now.AddSeconds(1);
        options.BudgetConfirmedAt = clock.Now;
        monitor.Set(options);
        first.Current.Enabled.ShouldBeTrue();
        using var restarted = new RailRuntime(monitor, clock);
        restarted.Current.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Retry_after_is_not_shortened_and_overflow_closes()
    {
        var clock = new Clock();
        var quota = new RailQuotaLedger(clock);
        quota.RemoteThrottled("120");
        clock.Now = clock.Now.AddSeconds(61);
        quota.TryDebit(100, 100).ShouldBeFalse();
        clock.Now = clock.Now.AddSeconds(60);
        quota.TryDebit(100, 100).ShouldBeTrue();
        quota.RemoteThrottled(new string('9', 50));
        clock.Now = clock.Now.AddYears(1);
        quota.TryDebit(100, 100).ShouldBeFalse();
    }

    [Fact]
    public async Task Terminal_client_dispatches_only_allowlisted_method_and_auth_header()
    {
        var terminal = new Terminal();
        var runtime = new RailRuntime(
            new Monitor(new RailOptions { Enabled = true, Fictional = true })
        );
        var client = new YandexRailClient(
            new HttpClient(terminal),
            runtime,
            new RailQuotaLedger(TimeProvider.System),
            TimeProvider.System
        );
        var result = await client.GetAsync(
            "copyright",
            "",
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Dispose();
        terminal.Calls.ShouldBe(1);
        (
            await client.GetAsync(
                "https://evil.example",
                "",
                runtime.Current,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeTrue();
        terminal.Calls.ShouldBe(1);
        terminal.Status = HttpStatusCode.TooManyRequests;
        var failed = await client.GetAsync(
            "search",
            "",
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        failed.FirstError.Code.ShouldBe("supplier-quota");
        terminal.Calls.ShouldBe(2);
        (
            await client.GetAsync(
                "stations_list",
                "",
                runtime.Current,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("quota");
        terminal.Calls.ShouldBe(2);
    }
}
