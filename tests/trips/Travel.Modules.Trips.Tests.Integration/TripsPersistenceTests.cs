using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

[CollectionDefinition("TripsPostgreSql", DisableParallelization = true)]
public sealed class TripsPostgreSqlCollection;

[Collection("TripsPostgreSql")]
[Trait("Category", "Integration")]
public sealed class TripsPersistenceTests : IntegrationTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static OwnerIdentity Owner => TripsCodecProtectionTests.Owner;

    private TripsDbContext Db()
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(ConnectionString);
        TripsDbContextConfiguration.Configure(options);
        return new(options.Options);
    }

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync(Ct);
    }

    private static TripPlanRecord Record(Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            Owner,
            1,
            new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero),
            new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero),
            "fictional-protected-envelope"
        );

    [Fact]
    public async Task Concurrent_last_slot_admission_allows_exactly_one_hundred_and_owner_isolation()
    {
        await using (var seed = Db())
        {
            for (var i = 0; i < 99; i++)
            {
                var row = Record();
                seed.Plans.Add(
                    new TripPlanEntity
                    {
                        TripId = row.TripId,
                        OwnerIssuer = row.Owner.Issuer,
                        OwnerSubject = row.Owner.Subject,
                        Revision = 1,
                        CreatedAt = row.CreatedAt,
                        UpdatedAt = row.UpdatedAt,
                        ProtectedPayload = row.ProtectedPayload,
                    }
                );
            }
            await seed.SaveChangesAsync(Ct);
        }
        var attempts = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(async _ =>
                {
                    await using var db = Db();
                    return await new TripPlanStore(db).TryCreateAsync(Record(), 100, Ct);
                })
        );
        attempts.Count(x => !x.IsError).ShouldBe(1);
        attempts
            .Count(x => x.IsError && x.FirstError.Code == "Trips.CapacityExceeded")
            .ShouldBe(11);
        await using var verify = Db();
        (await verify.Plans.CountAsync(Ct)).ShouldBe(100);
        var foreign = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        var store = new TripPlanStore(verify);
        (await store.ListAsync(foreign, null, 20, Ct)).Value.Items.ShouldBeEmpty();
        (
            await store.TryCreateAsync(Record() with { Owner = foreign }, 100, Ct)
        ).IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Atomic_cas_noop_stale_update_delete_race_and_no_upsert()
    {
        var row = Record();
        await using (var db = Db())
            (await new TripPlanStore(db).TryCreateAsync(row, 100, Ct)).IsError.ShouldBeFalse();
        await using (var db = Db())
        {
            var store = new TripPlanStore(db);
            (await store.TryReplaceAsync(1, row, Ct)).IsError.ShouldBeFalse();
            (await store.LoadAsync(Owner, row.TripId, Ct)).Value.ShouldBe(row);
        }
        var changed = row with
        {
            Revision = 2,
            UpdatedAt = row.UpdatedAt.AddMinutes(1),
            ProtectedPayload = "changed envelope",
        };
        var race = await Task.WhenAll(
            Task.Run(
                async () =>
                {
                    await using var db = Db();
                    return await new TripPlanStore(db).TryReplaceAsync(1, changed, Ct);
                },
                Ct
            ),
            Task.Run(
                async () =>
                {
                    await using var db = Db();
                    return await new TripPlanStore(db).TryDeleteAsync(Owner, row.TripId, 1, Ct);
                },
                Ct
            )
        );
        race.Count(x => !x.IsError).ShouldBe(1);
        await using var verify = Db();
        var storeVerify = new TripPlanStore(verify);
        var current = await storeVerify.LoadAsync(Owner, row.TripId, Ct);
        if (current.Value is not null)
        {
            current.Value.Revision.ShouldBe(2);
            (await storeVerify.TryDeleteAsync(Owner, row.TripId, 1, Ct)).FirstError.Code.ShouldBe(
                "Trips.RevisionMismatch"
            );
            (await storeVerify.TryDeleteAsync(Owner, row.TripId, 2, Ct)).IsError.ShouldBeFalse();
        }
        (await storeVerify.TryReplaceAsync(1, changed, Ct)).FirstError.Code.ShouldBe(
            "Trips.NotFound"
        );
        (await storeVerify.LoadAsync(Owner, row.TripId, Ct)).Value.ShouldBeNull();
    }

    [Fact]
    public async Task Relational_check_failure_rolls_back_without_consuming_owner_slot()
    {
        await using var db = Db();
        var store = new TripPlanStore(db);
        var invalid = Record() with { Revision = 0 };
        (await store.TryCreateAsync(invalid, 1, Ct)).IsError.ShouldBeTrue();
        (await db.Plans.CountAsync(Ct)).ShouldBe(0);
        (await store.TryCreateAsync(Record(), 1, Ct)).IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Protected_service_roundtrip_and_owner_guard_survive_postgres_reload()
    {
        using var ring = new TripsTestRing();
        var codec = new TripPlanJsonCodec();
        Guid id;
        await using (var db = Db())
        {
            var service = new TripPlanService(
                new TripPlanStore(db),
                ring.Protector,
                codec,
                new TripPlanCursor(ring.Provider),
                TimeProvider.System
            );
            var created = await service.CreateAsync(
                Owner,
                new("private fictional plan", TripsCodecProtectionTests.Brief),
                Ct
            );
            created.IsError.ShouldBeFalse();
            id = created.Value.TripId;
        }
        await using (var db = Db())
        {
            var entity = await db.Plans.SingleAsync(Ct);
            entity.ProtectedPayload.ShouldNotContain("private fictional plan");
            entity.ProtectedPayload.ShouldNotContain("saint-petersburg");
            var service = new TripPlanService(
                new TripPlanStore(db),
                ring.Protector,
                codec,
                new TripPlanCursor(ring.Provider),
                TimeProvider.System
            );
            var loaded = await service.GetAsync(Owner, id, Ct);
            loaded.IsError.ShouldBeFalse();
            loaded.Value.Title.ShouldBe("private fictional plan");
            var denied = await service.GetAsync(
                OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value,
                id,
                Ct
            );
            denied.FirstError.Code.ShouldBe("Trips.NotFound");
            var noOp = await service.ReplaceAsync(
                Owner,
                id,
                1,
                new(
                    loaded.Value.Title,
                    loaded.Value.Brief.ToInput(),
                    loaded
                        .Value.Days.Select(day => new TripDayInput(
                            day.Date,
                            day.Blocks.Select(block => block.ToInput()).ToArray()
                        ))
                        .ToArray()
                ),
                Ct
            );
            noOp.IsError.ShouldBeFalse();
            noOp.Value.Revision.ShouldBe(1);
            noOp.Value.UpdatedAt.ShouldBe(loaded.Value.UpdatedAt);
        }
    }

    [Fact]
    public async Task Oversized_corrupt_payload_is_rejected_in_sql_before_materialization_and_missing_schema_stays_unready()
    {
        using var ring = new TripsTestRing();
        await using var db = Db();
        var store = new TripPlanStore(db);
        var row = Record();
        (
            await store.TryCreateAsync(
                row with
                {
                    ProtectedPayload = new string('A', 200001),
                },
                100,
                Ct
            )
        ).IsError.ShouldBeFalse();
        var loaded = await store.LoadAsync(Owner, row.TripId, Ct);
        loaded.IsError.ShouldBeFalse();
        loaded.Value!.ProtectedPayload.ShouldBeEmpty();
        var listed = await store.ListAsync(Owner, null, 20, Ct);
        listed.Value.Items.Single().ProtectedPayload.ShouldBeEmpty();
        var service = new TripPlanService(
            store,
            ring.Protector,
            new TripPlanJsonCodec(),
            new TripPlanCursor(ring.Provider),
            TimeProvider.System
        );
        (await service.GetAsync(Owner, row.TripId, Ct)).FirstError.Code.ShouldBe(
            "Trips.Unavailable"
        );
        (await service.ListAsync(Owner, null, Ct)).FirstError.Code.ShouldBe("Trips.Unavailable");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped<TripsDbContext>(_ => Db());
        using var container = services.BuildServiceProvider();
        var readiness = new Travel.Modules.Trips.Infrastructure.Readiness.TripsReadiness(
            container.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            ring.Provider
        );
        (await readiness.IsReadyAsync(Ct)).ShouldBeTrue();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE trips.trip_plans", Ct);
        (await readiness.IsReadyAsync(Ct)).ShouldBeFalse();
        (
            await db
                .Database.SqlQuery<int>(
                    $"SELECT COUNT(*)::integer AS \"Value\" FROM information_schema.tables WHERE table_schema='trips' AND table_name='trip_plans'"
                )
                .SingleAsync(Ct)
        ).ShouldBe(0);
    }

    [Fact]
    public async Task Same_timestamp_pages_translate_guid_boundary_with_owner_scope_and_real_cursor()
    {
        using var ring = new TripsTestRing();
        await using var db = Db();
        var store = new TripPlanStore(db);
        var service = new TripPlanService(
            store,
            ring.Protector,
            new TripPlanJsonCodec(),
            new TripPlanCursor(ring.Provider),
            new FixedTime()
        );
        var ids = new List<Guid>();
        for (var index = 0; index < 25; index++)
        {
            var created = await service.CreateAsync(
                Owner,
                new("fictional pagination " + index, TripsCodecProtectionTests.Brief),
                Ct
            );
            created.IsError.ShouldBeFalse();
            ids.Add(created.Value.TripId);
        }
        var foreign = Record() with
        {
            Owner = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value,
        };
        (await store.TryCreateAsync(foreign, 100, Ct)).IsError.ShouldBeFalse();
        var first = await service.ListAsync(Owner, null, Ct);
        first.IsError.ShouldBeFalse();
        first.Value.Items.Count.ShouldBe(20);
        first.Value.NextCursor.ShouldNotBeNull();
        first.Value.NextCursor!.Length.ShouldBeLessThanOrEqualTo(256);
        var second = await service.ListAsync(Owner, first.Value.NextCursor, Ct);
        second.IsError.ShouldBeFalse();
        second.Value.Items.Count.ShouldBe(5);
        second.Value.NextCursor.ShouldBeNull();
        first
            .Value.Items.Concat(second.Value.Items)
            .Select(item => item.TripId)
            .ShouldBe(ids.OrderBy(id => id));
        (
            await service.ListAsync(foreign.Owner, first.Value.NextCursor, Ct)
        ).FirstError.Code.ShouldBe("Trips.InvalidCursor");
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    }
}
