extern alias TravelAiApp;
using Alba;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.IntegrationContracts.AI.NlSearch;
using Wolverine;
using Wolverine.Nats;
using Xunit;

namespace Travel.Host.Tests.Integration.NlSearch;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class NlSearchFailureTransportTests
{
    [Theory(Timeout = 90_000)]
    [InlineData("null", 0)]
    [InlineData("malformed", 0)]
    [InlineData("null", 1)]
    [InlineData("null", 2)]
    [InlineData("null", 3)]
    [InlineData("null", 4)]
    [InlineData("pending", 0)]
    public async Task Failed_response_or_accounting_never_repeats_inference_over_actual_nats(
        string output,
        int failOnSave
    )
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await using var nats = new ContainerBuilder("library/nats:2.12")
            .WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
            .Build();
        await postgres.StartAsync(ct);
        await nats.StartAsync(ct);
        var natsUrl = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
        using var chat = new ResponseClient(output);
        var accounting = new FailedAccounting(failOnSave);
        var completion = new ReplyCompletionLogger();
        await using var ai = await AlbaHost.For<TravelAiApp::Program>(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:travel", postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:nats", natsUrl);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            if (failOnSave >= 3 || output == "pending")
            {
                builder.UseSetting("NlSearch:HandlerTimeoutMilliseconds", "250");
                builder.UseSetting("NlSearch:InferenceTimeoutMilliseconds", "150");
            }
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(chat);
                services.AddSingleton<ILogger<NlSearchRequested>>(completion);
                if (failOnSave > 0)
                    services.ConfigureDbContext<TravelAiApp::Travel.AI.Persistence.AiDbContext>(
                        options => options.AddInterceptors(accounting)
                    );
            });
        });
        using var sender = Microsoft
            .Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(NlSearchRequested).Assembly;
                options.UseNats(natsUrl);
                options.PublishMessage<NlSearchRequested>().ToNatsSubject("travel.ai.nl_search");
            })
            .Build();
        await sender.StartAsync(ct);
        try
        {
            var correlation = Guid.NewGuid();
            await using var scope = sender.Services.CreateAsyncScope();
            var reply = scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync<NlSearchParsed>(
                    new NlSearchRequested("fictional private route", correlation),
                    ct,
                    timeout: TimeSpan.FromSeconds(3)
                );
            if (failOnSave >= 3 || output == "pending")
            {
                await completion.Started.WaitAsync(TimeSpan.FromSeconds(5), ct);
                await completion.Completed.WaitAsync(TimeSpan.FromSeconds(2), ct);
                completion.Elapsed.ShouldBeLessThan(
                    TimeSpan.FromSeconds(1),
                    "the configured 250ms whole reply budget also bounds noncooperative storage"
                );
            }
            await Should.ThrowAsync<Exception>(() => reply);
            if (failOnSave is 5 or 6)
                accounting.HeldContext!.Model.ShouldNotBeNull(
                    "the operation must retain ownership of its live context while storage ignores cancellation"
                );
            chat.Calls.ShouldBe(
                failOnSave is 1 or 4 or 6 ? 0 : 1,
                "storage admission must precede one non-retryable model call"
            );
            await using var verify = ai.Services.CreateAsyncScope();
            var db =
                verify.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();
            var rows = await db
                .CostLedger.AsNoTracking()
                .Where(x => x.CorrelationId == correlation)
                .ToArrayAsync(ct);
            if (failOnSave is 1 or 4 or 6)
                rows.ShouldBeEmpty();
            else if (failOnSave is 2 or 3 or 5)
            {
                var row = rows.ShouldHaveSingleItem();
                row.Outcome.ShouldBe("Admitted");
                row.InputTokens.ShouldBeNull();
                row.OutputTokens.ShouldBeNull();
                row.CostUsd.ShouldBeNull();
            }
            else if (output == "pending")
            {
                var row = rows.ShouldHaveSingleItem();
                row.Outcome.ShouldBe("DeadlineExceeded");
                row.CostUsd.ShouldBeNull();
                await Should.ThrowAsync<Exception>(() =>
                    scope
                        .ServiceProvider.GetRequiredService<IMessageBus>()
                        .InvokeAsync<NlSearchParsed>(
                            new NlSearchRequested("another fictional route", Guid.NewGuid()),
                            ct,
                            timeout: TimeSpan.FromSeconds(3)
                        )
                );
                chat.Calls.ShouldBe(
                    1,
                    "a task ignoring cancellation retains the process lease after the handler scope ends"
                );
                chat.CompletePending();
                await chat.Pending;
                // Late completion only releases the lease, with no background scoped ledger update.
                (
                    await db
                        .CostLedger.AsNoTracking()
                        .SingleAsync(x => x.CorrelationId == correlation, ct)
                ).CostUsd.ShouldBeNull();
            }
            else
            {
                var row = rows.ShouldHaveSingleItem();
                row.Outcome.ShouldBe("InvalidOutput");
                row.InputTokens.ShouldBe(12);
                row.OutputTokens.ShouldBe(8);
            }
        }
        finally
        {
            accounting.Release();
            await sender.StopAsync(ct);
        }
    }

    [Theory(Timeout = 90_000)]
    [InlineData(5)]
    [InlineData(6)]
    public Task Noncooperative_storage_preserves_whole_reply_budget_and_context_ownership(
        int failOnSave
    ) => Failed_response_or_accounting_never_repeats_inference_over_actual_nats("null", failOnSave);

    private sealed class FailedAccounting(int failOnSave) : SaveChangesInterceptor
    {
        private int saves;
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public DbContext? HeldContext { get; private set; }

        public void Release() => released.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var count = Interlocked.Increment(ref saves);
            if (failOnSave is 5 or 6 && count == (failOnSave == 5 ? 2 : 1))
            {
                HeldContext = eventData.Context;
                await released.Task; // Deliberately ignores cancellation; only the test owns release.
            }
            if (failOnSave is 3 or 4 && count == (failOnSave == 3 ? 2 : 1))
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (count == failOnSave)
                throw new InvalidOperationException("Fictional accounting outage");
            return result;
        }
    }

    private sealed class ReplyCompletionLogger : ILogger<NlSearchRequested>
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public Task Completed => completed.Task;
        public Task Started => started.Task;
        private long began;
        private long ended;
        public TimeSpan Elapsed => System.Diagnostics.Stopwatch.GetElapsedTime(began, ended);

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (eventId.Id == 1000)
            {
                began = System.Diagnostics.Stopwatch.GetTimestamp();
                started.TrySetResult();
            }
            if (eventId.Id == 1001)
            {
                ended = System.Diagnostics.Stopwatch.GetTimestamp();
                completed.TrySetResult();
            }
        }
    }

    private sealed class ResponseClient(string output) : IChatClient
    {
        private int calls;
        private readonly TaskCompletionSource<ChatResponse> pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public Task<ChatResponse> Pending => pending.Task;

        public void CompletePending() =>
            pending.TrySetResult(new(new ChatMessage(ChatRole.Assistant, "null")));

        public int Calls => Volatile.Read(ref calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref calls);
            if (output == "pending")
                return pending.Task;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, output))
                {
                    ModelId = "claude-opus-4-7",
                    Usage = new() { InputTokenCount = 12, OutputTokenCount = 8 },
                }
            );
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
