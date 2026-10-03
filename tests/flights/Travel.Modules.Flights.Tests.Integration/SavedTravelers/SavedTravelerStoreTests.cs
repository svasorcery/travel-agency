using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Shared.TestInfrastructure;
using Travel.Tests.Fixtures;

namespace Travel.Modules.Flights.Tests.Integration.SavedTravelers;

// CI-only real PostgreSQL coverage. Compile locally; never run this fixture locally.
[Trait("Category", "Integration")]
public sealed class SavedTravelerStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProtectedSavedTravelerSnapshot Envelope = ProtectedSavedTravelerSnapshot
        .Create(1, "Y2lwaGVydGV4dA==")
        .Value;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly CommandCapture _capture = new();

    private FlightsDbContext Context() =>
        new(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql(
                    ConnectionString,
                    options => options.MigrationsHistoryTable("__ef_migrations_history", "flights")
                )
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(_capture)
                .Options
        );

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        _capture.Clear();
    }

    [Fact]
    public async Task Owner_filtered_reads_and_metadata_visibility_never_return_foreign_ciphertext()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var store = new SavedTravelerStore(db);
        var record = Record();
        (await store.CreateAsync(record, ct)).Value.ShouldBe(SavedTravelerWriteOutcome.Succeeded);
        _capture.Clear();
        (await store.GetVisibilityAsync(Guid.NewGuid(), record.Id, ct)).Value.ShouldBe(
            SavedTravelerVisibility.Foreign
        );
        _capture.Commands.Single().ShouldNotContain("protected_details_json");
        _capture.Clear();
        (await store.GetOwnedAsync(Guid.NewGuid(), record.Id, ct)).Value.ShouldBeNull();
        var sql = _capture.Commands.Single();
        sql.ShouldContain("WHERE");
        sql.ShouldContain("owner_user_id");
        var protector = new NeverProtector();
        var service = new SavedTravelerService(store, protector, new FakeTimeProvider(Now));
        (await service.GetAsync(Guid.NewGuid(), record.Id, ct)).FirstError.Code.ShouldBe(
            "Flights.TravelerNotFound"
        );
        (
            await service.UpdateAsync(Guid.NewGuid(), record.Id, record.Revision, Details(), ct)
        ).FirstError.Code.ShouldBe("Flights.TravelerNotFound");
        protector.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Real_protected_profile_stores_only_ciphertext_and_metadata_and_reads_back()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ring = new FlightPiiTestRing(); // This test's private fictional ring only.
        var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
        await using var db = Context();
        var service = new SavedTravelerService(
            new SavedTravelerStore(db),
            protector,
            new FakeTimeProvider(Now)
        );
        var id = new SavedTravelerId(Guid.NewGuid());
        var created = await service.CreateAsync(_owner, id, Details(), ct);
        created.IsError.ShouldBeFalse();
        var row = await db.SavedTravelers.AsNoTracking().SingleAsync(x => x.Id == id.Value, ct);
        row.Revision.ShouldBe(created.Value.Revision);
        row.CreatedAt.ShouldBe(Now);
        row.UpdatedAt.ShouldBe(Now);
        row.ToString().ShouldBe("SavedTravelerEntity");
        foreach (
            var sentinel in new[]
            {
                "Fiction",
                "Traveler",
                "2009-10-03",
                "fiction@example.test",
                "12025550123",
            }
        )
            row.ProtectedDetailsJson.ShouldNotContain(sentinel);
        var envelope = JsonSerializer.Deserialize<ProtectedSavedTravelerSnapshot>(
            row.ProtectedDetailsJson
        )!;
        protector
            .Unprotect(new(_owner, id.Value, row.Revision), envelope)
            .Value.ShouldBe(Details());
        (await service.GetAsync(_owner, id, ct)).Value.Details.ShouldBe(Details());
        // The EF entity has exactly six fields; no plaintext, email/name/DOB indices or FK.
        var metadata = db.Model.FindEntityType(row.GetType())!;
        metadata
            .GetProperties()
            .Select(x => x.Name)
            .OrderBy(x => x)
            .ShouldBe(
                new[]
                {
                    "CreatedAt",
                    "Id",
                    "OwnerUserId",
                    "ProtectedDetailsJson",
                    "Revision",
                    "UpdatedAt",
                }
            );
        metadata.GetForeignKeys().ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_collision_is_insert_only_and_classified_by_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record();
        await using var first = Context();
        await using var second = Context();
        var outcomes = await Task.WhenAll(
            new SavedTravelerStore(first).CreateAsync(record, ct),
            new SavedTravelerStore(second).CreateAsync(
                record with
                {
                    Revision = Guid.NewGuid(),
                },
                ct
            )
        );
        outcomes.Count(x => x.Value == SavedTravelerWriteOutcome.Succeeded).ShouldBe(1);
        outcomes.Count(x => x.Value == SavedTravelerWriteOutcome.PreconditionFailed).ShouldBe(1);
        await using var third = Context();
        (
            await new SavedTravelerStore(third).CreateAsync(
                record with
                {
                    OwnerUserId = Guid.NewGuid(),
                },
                ct
            )
        ).Value.ShouldBe(SavedTravelerWriteOutcome.NotFound);
        third.ChangeTracker.Entries().ShouldBeEmpty();
        (
            await third.SavedTravelers.SingleAsync(x => x.Id == record.Id.Value, ct)
        ).OwnerUserId.ShouldBe(_owner);
    }

    [Fact]
    public async Task Concurrent_updates_with_same_revision_have_one_winner_and_preserve_created_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record() with { CreatedAt = Now.AddDays(-2) };
        await using var seed = Context();
        (await new SavedTravelerStore(seed).CreateAsync(record, ct)).Value.ShouldBe(
            SavedTravelerWriteOutcome.Succeeded
        );
        await using var first = Context();
        await using var second = Context();
        var one = record with
        {
            Revision = Guid.NewGuid(),
            UpdatedAt = Now.AddMinutes(1),
            CreatedAt = Now.AddDays(1),
        };
        var two = record with
        {
            Revision = Guid.NewGuid(),
            UpdatedAt = Now.AddMinutes(2),
            CreatedAt = Now.AddDays(2),
        };
        var outcomes = await Task.WhenAll(
            new SavedTravelerStore(first).UpdateAsync(one, record.Revision, ct),
            new SavedTravelerStore(second).UpdateAsync(two, record.Revision, ct)
        );
        outcomes.Count(x => x.Value == SavedTravelerWriteOutcome.Succeeded).ShouldBe(1);
        outcomes.Count(x => x.Value == SavedTravelerWriteOutcome.PreconditionFailed).ShouldBe(1);
        var saved = await seed
            .SavedTravelers.AsNoTracking()
            .SingleAsync(x => x.Id == record.Id.Value, ct);
        saved.Revision.ShouldBe(
            outcomes[0].Value == SavedTravelerWriteOutcome.Succeeded ? one.Revision : two.Revision
        );
        saved.CreatedAt.ShouldBe(record.CreatedAt);
        saved.UpdatedAt.ShouldBe(
            outcomes[0].Value == SavedTravelerWriteOutcome.Succeeded ? one.UpdatedAt : two.UpdatedAt
        );
        JsonSerializer
            .Deserialize<ProtectedSavedTravelerSnapshot>(saved.ProtectedDetailsJson)
            .ShouldBe(Envelope);
    }

    [Fact]
    public async Task Update_delete_race_has_one_winner_and_never_resurrects_deleted_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record();
        await using var seed = Context();
        (await new SavedTravelerStore(seed).CreateAsync(record, ct)).Value.ShouldBe(
            SavedTravelerWriteOutcome.Succeeded
        );
        await using var first = Context();
        await using var second = Context();
        var next = record with { Revision = Guid.NewGuid() };
        var results = await Task.WhenAll(
            new SavedTravelerStore(first).UpdateAsync(next, record.Revision, ct),
            new SavedTravelerStore(second).DeleteAsync(_owner, record.Id, record.Revision, ct)
        );
        results.Count(x => x.Value == SavedTravelerWriteOutcome.Succeeded).ShouldBe(1);
        var stored = await seed
            .SavedTravelers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == record.Id.Value, ct);
        if (results[1].Value == SavedTravelerWriteOutcome.Succeeded)
        {
            results[0].Value.ShouldBe(SavedTravelerWriteOutcome.NotFound);
            stored.ShouldBeNull();
        }
        else
        {
            results[1].Value.ShouldBe(SavedTravelerWriteOutcome.PreconditionFailed);
            stored.ShouldNotBeNull();
            stored.Revision.ShouldBe(next.Revision);
        }
    }

    [Fact]
    public async Task Stale_and_foreign_update_delete_cannot_change_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record();
        await using var db = Context();
        var store = new SavedTravelerStore(db);
        (await store.CreateAsync(record, ct)).IsError.ShouldBeFalse();
        (
            await store.UpdateAsync(record with { Revision = Guid.NewGuid() }, Guid.NewGuid(), ct)
        ).Value.ShouldBe(SavedTravelerWriteOutcome.PreconditionFailed);
        (
            await store.UpdateAsync(
                record with
                {
                    OwnerUserId = Guid.NewGuid(),
                },
                record.Revision,
                ct
            )
        ).Value.ShouldBe(SavedTravelerWriteOutcome.NotFound);
        (await store.DeleteAsync(_owner, record.Id, Guid.NewGuid(), ct)).Value.ShouldBe(
            SavedTravelerWriteOutcome.PreconditionFailed
        );
        (await store.DeleteAsync(Guid.NewGuid(), record.Id, record.Revision, ct)).Value.ShouldBe(
            SavedTravelerWriteOutcome.NotFound
        );
        (await store.GetOwnedAsync(_owner, record.Id, ct)).Value.ShouldBe(record);
    }

    [Fact]
    public async Task Delete_succeeds_with_unreadable_envelope_and_unavailable_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record();
        await using var db = Context();
        var store = new SavedTravelerStore(db);
        (await store.CreateAsync(record, ct)).IsError.ShouldBeFalse();
        await db
            .SavedTravelers.Where(x => x.Id == record.Id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProtectedDetailsJson, "{}"), ct);
        var protector = new NeverProtector();
        var service = new SavedTravelerService(store, protector, new FakeTimeProvider(Now));
        (await service.DeleteAsync(_owner, record.Id, record.Revision, ct)).IsError.ShouldBeFalse();
        protector.Calls.ShouldBe(0);
        (await store.GetVisibilityAsync(_owner, record.Id, ct)).Value.ShouldBe(
            SavedTravelerVisibility.Missing
        );
    }

    [Fact]
    public async Task Owner_page_is_bounded_to_twenty_one_and_stably_ordered_by_created_then_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var store = new SavedTravelerStore(db);
        for (var n = 1; n <= 45; n++)
        {
            var record = Record(new(Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}"))) with
            {
                CreatedAt = Now.AddSeconds(n / 5),
            };
            (await store.CreateAsync(record, ct)).IsError.ShouldBeFalse();
        }
        (
            await store.CreateAsync(
                Record() with
                {
                    OwnerUserId = Guid.NewGuid(),
                    CreatedAt = Now.AddDays(1),
                },
                ct
            )
        ).IsError.ShouldBeFalse();
        var expected = await db
            .SavedTravelers.Where(x => x.OwnerUserId == _owner)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        var first = (await store.ListOwnedAsync(_owner, 0, ct)).Value;
        first.Count.ShouldBe(21);
        first.Select(x => x.Id.Value).ShouldBe(expected.Take(21));
        var second = (await store.ListOwnedAsync(_owner, 20, ct)).Value;
        second.Count.ShouldBe(21);
        second.Select(x => x.Id.Value).ShouldBe(expected.Skip(20).Take(21));
        (await store.ListOwnedAsync(_owner, 40, ct)).Value.Count.ShouldBe(5);
        _capture.Commands.Last().ShouldContain("LIMIT");
        _capture.Commands.Last().ShouldContain("OFFSET");
    }

    [Fact]
    public async Task Malformed_owned_envelope_fails_closed_but_visibility_and_delete_still_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record();
        await using var db = Context();
        var store = new SavedTravelerStore(db);
        (await store.CreateAsync(record, ct)).IsError.ShouldBeFalse();
        await db
            .SavedTravelers.Where(x => x.Id == record.Id.Value)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(
                        x => x.ProtectedDetailsJson,
                        "{\"Ciphertext\":null,\"FormatVersion\":1}"
                    ),
                ct
            );
        (await store.GetOwnedAsync(_owner, record.Id, ct)).FirstError.Code.ShouldBe(
            "Flights.PiiPayloadUnavailable"
        );
        (await store.ListOwnedAsync(_owner, 0, ct)).FirstError.Code.ShouldBe(
            "Flights.PiiPayloadUnavailable"
        );
        (await store.GetVisibilityAsync(_owner, record.Id, ct)).Value.ShouldBe(
            SavedTravelerVisibility.Owned
        );
        (await store.DeleteAsync(_owner, record.Id, record.Revision, ct)).Value.ShouldBe(
            SavedTravelerWriteOutcome.Succeeded
        );
    }

    [Fact]
    public async Task Rollback_removes_only_profile_table_and_reapply_recreates_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var migrations = db.Database.GetMigrations().ToArray();
        migrations.Last().ShouldContain("SavedTravelers");
        var prior = migrations[^2];
        var migrator = db.GetService<IMigrator>();
        var downSql = migrator.GenerateScript(migrations[^1], prior);
        downSql.ShouldContain("DROP TABLE flights.saved_travelers");
        downSql.ShouldNotContain("order_read_model");
        var before = await db.Orders.CountAsync(ct);
        await migrator.MigrateAsync(prior, ct);
        (await db.Orders.CountAsync(ct)).ShouldBe(before);
        await db.Database.MigrateAsync(ct);
        (await new SavedTravelerStore(db).ListOwnedAsync(_owner, 0, ct)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unique_collision_detaches_only_failed_profile_entity()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = Record();
        await using var seed = Context();
        (await new SavedTravelerStore(seed).CreateAsync(record, ct)).IsError.ShouldBeFalse();
        await using var db = Context();
        var unrelated =
            new Travel.Modules.Flights.Infrastructure.Persistence.Entities.SavedTravelerEntity
            {
                Id = Guid.NewGuid(),
                OwnerUserId = _owner,
                Revision = Guid.NewGuid(),
                ProtectedDetailsJson = JsonSerializer.Serialize(Envelope),
                CreatedAt = Now,
                UpdatedAt = Now,
            };
        db.SavedTravelers.Add(unrelated);
        (await new SavedTravelerStore(db).CreateAsync(record, ct)).Value.ShouldBe(
            SavedTravelerWriteOutcome.PreconditionFailed
        );
        db.Entry(unrelated).State.ShouldBe(EntityState.Added);
        db.ChangeTracker.Entries<Travel.Modules.Flights.Infrastructure.Persistence.Entities.SavedTravelerEntity>()
            .Select(x => x.Entity)
            .ShouldBe([unrelated]);
        // Entire failed SaveChanges transaction was rolled back; no unrelated insertion committed.
        (
            await seed.SavedTravelers.AsNoTracking().AnyAsync(x => x.Id == unrelated.Id, ct)
        ).ShouldBeFalse();
    }

    [Fact]
    public async Task Database_failure_returns_safe_storage_error_without_exception_metadata()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        // Dedicated container only: deliberately simulate unavailable storage, then dispose it.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE flights.saved_travelers", ct);
        var result = await new SavedTravelerStore(db).ListOwnedAsync(_owner, 0, ct);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.TravelerStorageUnavailable");
        result.FirstError.Description.ShouldBe("Saved traveler storage is unavailable.");
        result.FirstError.Metadata.ShouldBeNull();
    }

    private SavedTravelerStoredRecord Record(SavedTravelerId? id = null) =>
        new(id ?? new(Guid.NewGuid()), _owner, Guid.NewGuid(), Envelope, Now, Now);

    private static SavedTravelerDetails Details() =>
        SavedTravelerDetails
            .CreateRaw(
                "mr",
                "Fiction",
                "Traveler",
                new(2009, 10, 3),
                "male",
                "fiction@example.test",
                "+12025550123",
                new(2026, 10, 3)
            )
            .Value;

    private sealed class NeverProtector : ISavedTravelerProtector
    {
        public int Calls;

        public ErrorOr<ProtectedSavedTravelerSnapshot> Protect(
            SavedTravelerProtectionContext context,
            SavedTravelerDetails details
        )
        {
            Calls++;
            return PiiProtectionErrors.Unavailable;
        }

        public ErrorOr<SavedTravelerDetails> Unprotect(
            SavedTravelerProtectionContext context,
            ProtectedSavedTravelerSnapshot snapshot
        )
        {
            Calls++;
            return PiiProtectionErrors.PayloadUnavailable;
        }
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();
        public IReadOnlyList<string> Commands => _commands.ToArray();

        public void Clear() => _commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
