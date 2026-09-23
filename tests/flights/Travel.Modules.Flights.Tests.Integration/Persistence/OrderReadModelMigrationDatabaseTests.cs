using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Shared.TestInfrastructure;

namespace Travel.Modules.Flights.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class OrderReadModelMigrationDatabaseTests : IntegrationTestBase
{
    private const string Baseline = "20260513153403_FlightsM1Init";
    private const string Checkpoint = "20260922132058_AddOrderReadModelProjectedStreamVersion";

    private FlightsDbContext CreateContext()
    {
        // Reuse module-owned provider/naming/history configuration. Replace the design-time
        // connection before any database operation; only this fixture's container is accessed.
        var db = FlightsDbContextOptionsParityTests.CreateDesignTimeContext();
        db.Database.SetConnectionString(ConnectionString);
        return db;
    }

    [Fact]
    public async Task Fresh_migration_preserves_explicit_zero_and_positive_versions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.MigrateAsync(ct);

        var untrusted = Order();
        var zero = Order();
        zero.ProjectedStreamVersion = 0;
        var projected = Order();
        projected.ProjectedStreamVersion = 7;
        db.Orders.AddRange(untrusted, zero, projected);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        (
            await db.Orders.SingleAsync(x => x.Id == untrusted.Id, ct)
        ).ProjectedStreamVersion.ShouldBe(-1);
        (await db.Orders.SingleAsync(x => x.Id == zero.Id, ct)).ProjectedStreamVersion.ShouldBe(0);
        (
            await db.Orders.SingleAsync(x => x.Id == projected.Id, ct)
        ).ProjectedStreamVersion.ShouldBe(7);
        var view = await new OrderReadModelQueries(db).GetAsync(
            projected.AggregateId,
            projected.UserId!.Value,
            ct
        );
        view.ShouldNotBeNull();
        view.ProjectedStreamVersion.ShouldBe(7);
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe([Baseline, Checkpoint]);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Upgrade_SQL_is_repeatable_and_preserves_existing_order_with_untrusted_checkpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Baseline, ct);
        var legacy = Order();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO flights.order_read_model
                (id, aggregate_id, user_id, provider_order_id, status, total_amount,
                 currency, itinerary_json, passenger_info_json, ticket_numbers, booked_at)
            VALUES
                ({legacy.Id}, {legacy.AggregateId}, {legacy.UserId}, {legacy.ProviderOrderId},
                 {legacy.Status}, {legacy.TotalAmount}, {legacy.Currency}, jsonb_build_object(),
                 jsonb_build_object(), ARRAY['TKT-LEGACY']::text[], {legacy.BookedAt});
            """,
            ct
        );

        var sql = migrator.GenerateScript(
            Baseline,
            Checkpoint,
            MigrationsSqlGenerationOptions.Idempotent
        );
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        await db.Database.ExecuteSqlRawAsync(sql, ct);

        var restored = await db.Orders.AsNoTracking().SingleAsync(ct);
        restored.Id.ShouldBe(legacy.Id);
        restored.AggregateId.ShouldBe(legacy.AggregateId);
        restored.UserId.ShouldBe(legacy.UserId);
        restored.ProviderOrderId.ShouldBe(legacy.ProviderOrderId);
        restored.Status.ShouldBe(legacy.Status);
        restored.TotalAmount.ShouldBe(legacy.TotalAmount);
        restored.Currency.ShouldBe(legacy.Currency);
        restored.ItineraryJson.ShouldBe("{}");
        restored.PassengerInfoJson.ShouldBe("{}");
        restored.TicketNumbers.ShouldBe(["TKT-LEGACY"]);
        restored.BookedAt.ShouldBe(legacy.BookedAt);
        restored.ProjectedStreamVersion.ShouldBe(-1);
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe([Baseline, Checkpoint]);
    }

    [Fact]
    public async Task Stale_checkpoint_update_cannot_overwrite_winning_fields_or_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order();
        order.ProjectedStreamVersion = 3;
        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync(ct);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync(ct);
        }

        await using var winner = CreateContext();
        await using var loser = CreateContext();
        var first = await winner.Orders.SingleAsync(ct);
        var second = await loser.Orders.SingleAsync(ct);
        first.Status = "Confirmed";
        first.ProjectedStreamVersion = 4;
        await winner.SaveChangesAsync(ct);
        second.Status = "Cancelled";
        second.ProjectedStreamVersion = 5;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync(ct));

        await using var verify = CreateContext();
        var persisted = await verify.Orders.AsNoTracking().SingleAsync(ct);
        persisted.Id.ShouldBe(order.Id);
        persisted.Status.ShouldBe("Confirmed");
        persisted.ProjectedStreamVersion.ShouldBe(4);
    }

    private static OrderReadModelEntity Order() =>
        new()
        {
            Id = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ProviderOrderId = "ord-migration-test",
            Status = "Held",
            TotalAmount = 123.45m,
            Currency = "USD",
            ItineraryJson = "{}",
            PassengerInfoJson = "{}",
            BookedAt = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero),
        };
}
