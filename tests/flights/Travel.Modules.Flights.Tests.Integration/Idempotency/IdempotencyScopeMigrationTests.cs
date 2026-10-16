using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Idempotency;

[Trait("Category", "Integration")]
public sealed class IdempotencyScopeMigrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Scope_migration_preserves_receipts_and_rollback_refuses_overlapping_keys_without_data_loss()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new FlightsDbContext(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql(
                    ConnectionString,
                    options => options.MigrationsHistoryTable("__ef_migrations_history", "flights")
                )
                .UseSnakeCaseNamingConvention()
                .Options
        );
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261003115745_AddSavedTravelers", ct);
        var owner = Guid.NewGuid();
        // Seed the historical schema with SQL: the current EF model deliberately has a different key.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO flights.idempotency_keys
                (key,user_id,route,body_hash,response_hash,response_status,response_body,created_at,expires_at)
            VALUES ('retained-key',{owner},'/hold','body','receipt',200,'fictional receipt',now(),now()+interval '24 hours')
            """,
            ct
        );
        await migrator.MigrateAsync("20261016090418_ScopeIdempotencyKeys", ct);
        var store = new IdempotencyStore(db, new FakeTimeProvider(DateTimeOffset.UtcNow));
        (
            await store.TryGetAsync(new IdempotencyKey("retained-key"), owner, "/hold", "body", ct)
        )!.ResponseBody.ShouldBe("fictional receipt");
        await store.SaveAsync(
            new IdempotencyKey("retained-key"),
            owner,
            "/confirm",
            "body2",
            "receipt2",
            200,
            "second receipt",
            ct
        );
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            migrator.MigrateAsync("20261003115745_AddSavedTravelers", ct)
        );
        failure.MessageText.ShouldContain("retained scoped keys overlap");
        (await db.IdempotencyKeys.AsNoTracking().CountAsync(ct)).ShouldBe(2);
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldContain(
            "20261016090418_ScopeIdempotencyKeys"
        );
        await db.IdempotencyKeys.Where(x => x.Route == "/confirm").ExecuteDeleteAsync(ct);
        await migrator.MigrateAsync("20261003115745_AddSavedTravelers", ct);
        (await db.IdempotencyKeys.AsNoTracking().SingleAsync(ct)).ResponseBody.ShouldBe(
            "fictional receipt"
        );
    }
}
