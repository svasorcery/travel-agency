using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.NlSearch;
using Xunit;

namespace Travel.AI.Tests.NlSearch;

public sealed class NlSearchAdmissionTests
{
    [Fact]
    public async Task Ignored_cancellation_has_a_bounded_reply_and_keeps_the_active_lease_until_settlement()
    {
        var time = new FakeTimeProvider();
        var options = new NlSearchOptions();
        var admission = new NlSearchAdmission(options, time);
        var pending = new TaskCompletionSource<ChatResponse>();
        using var chat = new PendingChat(pending.Task);
        using var lease = admission.TryEnter(.03m);
        lease.ShouldNotBeNull();
        var extraction = NlSearchExtractor.ExtractAsync(
            chat,
            "Moscow to Kazan",
            new(2026, 10, 16),
            options,
            time,
            lease.DeferUntil,
            TestContext.Current.CancellationToken
        );
        chat.Calls.ShouldBe(1);
        time.Advance(TimeSpan.FromMilliseconds(3500));
        var error = await Should.ThrowAsync<NlSearchExtractionException>(() => extraction);
        error.Outcome.ShouldBe("DeadlineExceeded");
        lease.Dispose();
        admission.TryEnter(.03m).ShouldBeNull();
        pending.SetResult(new(new ChatMessage(ChatRole.Assistant, "null")));
        // Continuation belongs to the lease only; settlement never needs a scoped database.
        await pending.Task;
        await Task.Yield();
        using var next = admission.TryEnter(.03m);
        next.ShouldNotBeNull();
        chat.Calls.ShouldBe(1);
    }

    [Fact]
    public void Unknown_usage_keeps_the_reservation_across_minutes_and_known_usage_releases_only_the_difference()
    {
        var time = new FakeTimeProvider();
        var admission = new NlSearchAdmission(new() { HourlyBudgetUsd = .2m }, time);
        using (var first = admission.TryEnter(.03m))
        {
            first.ShouldNotBeNull();
            first.Settle(null);
        }
        using (var second = admission.TryEnter(.03m))
        {
            second.ShouldNotBeNull();
            second.Settle(.00175m);
        }
        // Another full .10 reservation would exceed .20 while unknown .10 remains.
        admission.TryEnter(.03m).ShouldBeNull();
        time.Advance(TimeSpan.FromMinutes(2));
        admission.TryEnter(.03m).ShouldBeNull();
        time.Advance(TimeSpan.FromHours(1));
        using var renewed = admission.TryEnter(.03m);
        renewed.ShouldNotBeNull();
    }

    [Fact]
    public void Rate_limit_counts_completed_calls_and_resets_after_the_rolling_minute()
    {
        var time = new FakeTimeProvider();
        var admission = new NlSearchAdmission(new(), time);
        for (var i = 0; i < 6; i++)
        {
            using var lease = admission.TryEnter(.03m);
            lease.ShouldNotBeNull();
            lease.Settle(.001m);
        }
        admission.TryEnter(.03m).ShouldBeNull();
        time.Advance(TimeSpan.FromMinutes(1));
        using var next = admission.TryEnter(.03m);
        next.ShouldNotBeNull();
    }

    [Fact]
    public void Invalid_configuration_fails_before_any_admission()
    {
        foreach (
            var invalid in new[]
            {
                new NlSearchOptions { MaxInputTokens = 0 },
                new() { HourlyBudgetUsd = 0 },
                new() { MaxOutputTokens = 513 },
                new() { InferenceTimeoutMilliseconds = 3501 },
                new() { MaxConcurrentRequests = 2 },
                new() { HandlerTimeoutMilliseconds = 1 },
                new() { RequestsPerMinute = 0 },
                new() { MaxCostUsd = -.1m },
            }
        )
            Should.Throw<InvalidOperationException>(() =>
                new NlSearchAdmission(invalid, TimeProvider.System)
            );
        NlSearchOptions.EstimateCost(100, 50).ShouldBe(.00175m);
    }

    private sealed class PendingChat(Task<ChatResponse> pending) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return pending;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
