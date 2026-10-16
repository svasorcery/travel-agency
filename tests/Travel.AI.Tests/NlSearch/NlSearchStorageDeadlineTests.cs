using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.NlSearch;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Travel.AI.Tests.Observability;
using Travel.IntegrationContracts.AI.NlSearch;
using Xunit;

namespace Travel.AI.Tests.NlSearch;

[Trait("Category", "Integration")]
public sealed class NlSearchStorageDeadlineTests
    : Travel.Shared.TestInfrastructure.IntegrationTestBase
{
    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = CreateDb();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private AiDbContext CreateDb(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention();
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Public_handler_bounds_noncooperative_storage_and_owns_context_until_settlement(
        int save
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider();
        var blocked = new NoncooperativeSave(save);
        using var services = new ServiceCollection()
            .AddScoped(_ => CreateDb(blocked))
            .BuildServiceProvider();
        var admission = new NlSearchAdmission(
            new() { HandlerTimeoutMilliseconds = 200, InferenceTimeoutMilliseconds = 100 },
            time
        );
        using var chat = new RawNlChatClient(
            "null",
            new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
        );
        var request = new NlSearchRequested("fictional route", Guid.NewGuid());
        var handler = NlSearchAiHandler.Handle(
            request,
            chat,
            services.GetRequiredService<IServiceScopeFactory>(),
            time,
            new AiMetrics(new TestMeterFactory()),
            NullLogger<NlSearchRequested>.Instance,
            admission,
            ct
        );
        try
        {
            await blocked.Started.WaitAsync(TimeSpan.FromSeconds(2), ct);
            // Establish the blocked storage stage before advancing the deadline;
            // cold EF/PostgreSQL setup must not consume the scenario's budget.
            handler.IsCompleted.ShouldBeFalse();
            time.Advance(TimeSpan.FromMilliseconds(199));
            handler.IsCompleted.ShouldBeFalse("the 200ms handler deadline has not elapsed");
            time.Advance(TimeSpan.FromMilliseconds(1));
            var error = await Should.ThrowAsync<NlSearchExtractionException>(() =>
                handler.WaitAsync(TimeSpan.FromSeconds(1), ct)
            );
            error.Outcome.ShouldBe("DeadlineExceeded");
            blocked.Context!.Model.ShouldNotBeNull(
                "owned context stays alive after the outer reply ends"
            );
            admission.TryEnter(.03m).ShouldBeNull("storage still owns the active lease");
            chat.Calls.ShouldBe(save == 1 ? 0 : 1);
            await using var verify = CreateDb();
            var rows = await verify
                .CostLedger.AsNoTracking()
                .Where(x => x.CorrelationId == request.CorrelationId)
                .ToArrayAsync(ct);
            if (save == 1)
                rows.ShouldBeEmpty();
            else
            {
                var row = rows.ShouldHaveSingleItem();
                row.Outcome.ShouldBe("Admitted");
                row.CostUsd.ShouldBeNull();
            }
        }
        finally
        {
            blocked.Release();
            _ = await Record.ExceptionAsync(async () =>
                await handler.WaitAsync(TimeSpan.FromSeconds(2), ct)
            );
        }
        NlSearchAdmission.Lease? released = null;
        SpinWait
            .SpinUntil(
                () => (released = admission.TryEnter(.03m)) is not null,
                TimeSpan.FromSeconds(2)
            )
            .ShouldBeTrue();
        released!.Dispose();
        chat.Calls.ShouldBe(save == 1 ? 0 : 1, "late pre-save completion must not start inference");
    }

    private sealed class NoncooperativeSave(int save) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int calls;
        public Task Started => started.Task;
        public DbContext? Context { get; private set; }

        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref calls) == save)
            {
                Context = eventData.Context;
                started.TrySetResult();
                await release.Task;
            }
            return result;
        }
    }
}
