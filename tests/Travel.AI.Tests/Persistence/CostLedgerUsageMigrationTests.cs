using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.AI.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class CostLedgerUsageMigrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Rollback_guard_blocks_a_concurrent_writer_until_the_guarded_DDL_finishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var barrier = new RollbackGuardBarrier();
        DbContextOptions<AiDbContext> Options(bool intercept) =>
            new DbContextOptionsBuilder<AiDbContext>()
                .UseNpgsql(ConnectionString, AiDbContextConfiguration.ConfigureNpgsql)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(intercept ? [barrier] : [])
                .Options;
        await using var migrating = new AiDbContext(Options(true));
        await migrating.Database.MigrateAsync(ct);
        var down = migrating
            .GetService<IMigrator>()
            .MigrateAsync("20260813200652_CostLedgerMessageIdempotency", ct);
        await barrier.GuardChecked.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await using var writer = new AiDbContext(Options(false));
        await writer.Database.OpenConnectionAsync(ct);
        var writerPid = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
        writer.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.nl_search",
                Model = "fictional-model",
                InputTokens = 12,
                OutputTokens = 8,
                CostUsd = 0.00026m,
                Outcome = "Ready",
                MessageIdentity = "concurrent-test",
                CorrelationId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
            }
        );
        var insert = writer.SaveChangesAsync(ct);
        try
        {
            await using var observer = new NpgsqlConnection(ConnectionString);
            await observer.OpenAsync(ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (!insert.IsCompleted)
            {
                await using var command = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid=@pid AND wait_event_type='Lock')",
                    observer
                );
                command.Parameters.AddWithValue("pid", writerPid);
                if ((bool)(await command.ExecuteScalarAsync(deadline.Token))!)
                    break;
                await Task.Delay(10, deadline.Token);
            }
            insert.IsCompleted.ShouldBeFalse(
                "a writer cannot commit a new outcome between the guard and DROP COLUMN"
            );
        }
        finally
        {
            barrier.Release.TrySetResult();
            await down;
        }
        var failure = await Should.ThrowAsync<DbUpdateException>(() => insert);
        failure
            .InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UndefinedColumn);
    }

    [Fact]
    public async Task Historical_numbers_remain_unverified_and_rollback_cannot_erase_unknown_usage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new AiDbContext(
            new DbContextOptionsBuilder<AiDbContext>()
                .UseNpgsql(ConnectionString, AiDbContextConfiguration.ConfigureNpgsql)
                .UseSnakeCaseNamingConvention()
                .Options
        );
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260813200652_CostLedgerMessageIdempotency", ct);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ai.cost_ledger (id,feature,model,input_tokens,output_tokens,cost_usd,message_identity,correlation_id,occurred_at)
            VALUES ({id},'flights.nl_search','historical-model',0,12,0.004,'historical-identity',{Guid.NewGuid()},now())
            """,
            ct
        );
        await migrator.MigrateAsync("20261016091213_CostLedgerUsageOutcomes", ct);
        var historical = await db.CostLedger.AsNoTracking().SingleAsync(ct);
        historical.Id.ShouldBe(id);
        historical.InputTokens.ShouldBe(0);
        historical.OutputTokens.ShouldBe(12);
        historical.CostUsd.ShouldBe(0.004m);
        historical.Outcome.ShouldBe("LegacyRecorded");
        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.nl_search",
                Model = "fictional-model",
                InputTokens = 10,
                OutputTokens = null,
                CostUsd = null,
                Outcome = "UsageUnknown",
                MessageIdentity = "unknown-test",
                CorrelationId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            migrator.MigrateAsync("20260813200652_CostLedgerMessageIdempotency", ct)
        );
        failure.MessageText.ShouldContain("retained accounting records");
        (await db.CostLedger.AsNoTracking().CountAsync(ct)).ShouldBe(2);
        (
            await db.CostLedger.AsNoTracking().SingleAsync(x => x.Outcome == "UsageUnknown", ct)
        ).CostUsd.ShouldBeNull();
        await db.CostLedger.Where(x => x.Outcome == "UsageUnknown").ExecuteDeleteAsync(ct);
        await migrator.MigrateAsync("20260813200652_CostLedgerMessageIdempotency", ct);
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldNotContain(
            "20261016091213_CostLedgerUsageOutcomes"
        );
    }

    private sealed class RollbackGuardBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource GuardChecked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains(
                    "Cannot remove usage outcomes",
                    StringComparison.Ordinal
                )
            )
            {
                GuardChecked.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
