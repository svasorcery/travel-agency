using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.Persistence;
using Travel.AI.TripsBriefExtraction;
using Travel.Shared.TestInfrastructure;
using Xunit;
using static Travel.AI.Tests.TripsBriefExtraction.TripsBriefExtractorTests;

namespace Travel.AI.Tests.TripsBriefExtraction;

[Trait("Category", "Integration")]
public sealed class TripsBriefLedgerTests : IntegrationTestBase
{
    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = CreateDb();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    private AiDbContext CreateDb(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(ConnectionString, AiDbContextConfiguration.ConfigureNpgsql)
            .UseSnakeCaseNamingConvention();
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    [Fact]
    public async Task Real_ledger_commits_unknown_admission_before_dispatch_and_updates_metadata_only()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var request = Request(time, options);
        using var services = new ServiceCollection()
            .AddScoped(_ => CreateDb())
            .BuildServiceProvider();
        var ledger = new TripsBriefLedger(
            services.GetRequiredService<IServiceScopeFactory>(),
            time
        );
        var chat = new FakeChat
        {
            Before = () =>
            {
                using var verify = CreateDb();
                var row = verify.CostLedger.Single(r => r.CorrelationId == request.CorrelationId);
                row.Outcome.ShouldBe("Admitted");
                row.InputTokens.ShouldBeNull();
                row.OutputTokens.ShouldBeNull();
                row.CostUsd.ShouldBeNull();
                row.UserId.ShouldBeNull();
            },
        };
        var result = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(request, ledger, TestContext.Current.CancellationToken);
        result.Status.ShouldBe("Ready");
        await using var db = CreateDb();
        var recorded = await db
            .CostLedger.AsNoTracking()
            .SingleAsync(
                r => r.CorrelationId == request.CorrelationId,
                TestContext.Current.CancellationToken
            );
        recorded.Feature.ShouldBe("trips.brief_extraction");
        recorded.Model.ShouldBe(options.Model);
        recorded.Outcome.ShouldBe("Ready");
        recorded.CostUsd.ShouldBe(.000015m);
        recorded.InputTokens.ShouldBe(10);
        recorded.UserId.ShouldBeNull();
        System.Text.Json.JsonSerializer.Serialize(recorded).ShouldNotContain(request.Text);
    }

    [Fact]
    public async Task Missing_usage_keeps_unknown_cost_in_existing_nullable_schema()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var request = Request(time, options);
        using var services = new ServiceCollection()
            .AddScoped(_ => CreateDb())
            .BuildServiceProvider();
        var ledger = new TripsBriefLedger(
            services.GetRequiredService<IServiceScopeFactory>(),
            time
        );
        var result = await new TripsBriefExtractor(
            new FakeChat { Usage = null },
            options,
            new(options, time),
            time
        ).ExtractAsync(request, ledger, TestContext.Current.CancellationToken);
        result.Status.ShouldBe("AccountingUnavailable");
        await using var db = CreateDb();
        var recorded = await db
            .CostLedger.AsNoTracking()
            .SingleAsync(
                r => r.CorrelationId == request.CorrelationId,
                TestContext.Current.CancellationToken
            );
        recorded.CostUsd.ShouldBeNull();
        recorded.InputTokens.ShouldBeNull();
        recorded.OutputTokens.ShouldBeNull();
        recorded.Outcome.ShouldBe("AccountingUnavailable");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Noncooperative_database_owns_context_until_settlement_and_retains_active_lease(
        int save
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider();
        var options = Enabled();
        var request = Request(time, options);
        var blocked = new NoncooperativeSave(save);
        using var services = new ServiceCollection()
            .AddScoped(_ => CreateDb(blocked))
            .BuildServiceProvider();
        var ledger = new TripsBriefLedger(
            services.GetRequiredService<IServiceScopeFactory>(),
            time
        );
        var admission = new TripsBriefAdmission(options, time);
        var chat = new FakeChat();
        var handling = new TripsBriefExtractor(chat, options, admission, time).ExtractAsync(
            request,
            ledger,
            ct
        );
        try
        {
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            time.Advance(TimeSpan.FromMilliseconds(4500));
            var result = await handling.WaitAsync(TimeSpan.FromSeconds(2), ct);
            result.Status.ShouldBe("AccountingUnavailable");
            result.Fields.ShouldBeNull();
            admission.TryEnter(.001m).ShouldBeNull();
            blocked.Context!.Model.ShouldNotBeNull(
                "the owned database context is not disposed by the bounded observer"
            );
            chat.Calls.ShouldBe(save == 1 ? 0 : 1);
            await using var db = CreateDb();
            var rows = await db
                .CostLedger.AsNoTracking()
                .Where(r => r.CorrelationId == request.CorrelationId)
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
        }
        await blocked.Settled.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        TripsBriefAdmission.Lease? next = null;
        SpinWait
            .SpinUntil(
                () => (next = admission.TryEnter(.001m)) is not null,
                TimeSpan.FromSeconds(3)
            )
            .ShouldBeTrue();
        next!.Dispose();
    }

    private sealed class NoncooperativeSave(int blockCall) : SaveChangesInterceptor
    {
        private int calls;
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Settled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DbContext? Context;

        public void Release() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref calls) == blockCall)
            {
                Context = eventData.Context;
                Started.TrySetResult();
                await release.Task;
                Settled.TrySetResult();
            }
            return result;
        }
    }
}
