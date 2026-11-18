using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

[Collection("TripsPostgreSql")]
[Trait("Category", "Integration")]
public sealed class TripFlightLinkPersistenceTests : IntegrationTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static OwnerIdentity Owner => TripsCodecProtectionTests.Owner;
    private readonly Guid trip = Guid.NewGuid();

    private TripsDbContext Db(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(ConnectionString);
        TripsDbContextConfiguration.Configure(options);
        options.AddInterceptors(interceptors);
        return new(options.Options);
    }

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        var row = new TripPlanRecord(trip, Owner, 1, now, now, "historical-protected-plan");
        (await new TripPlanStore(db).TryCreateAsync(row, 100, Ct)).IsError.ShouldBeFalse();
    }

    private TripFlightLinkBinding Binding(long revision) => new(Owner, trip, revision);

    private TripFlightLinkRecord Record(long revision) =>
        new(Binding(revision), "fictional-envelope");

    [Fact]
    public async Task Generated_down_and_up_scripts_preserve_the_historical_plan()
    {
        await using var db = Db();
        var planStore = new TripPlanStore(db);
        var saved = (await planStore.LoadAsync(Owner, trip, Ct)).Value!;
        var links = new TripFlightLinkStore(db);
        (await links.CommitAsync(Binding(0), Record(1), Ct)).IsError.ShouldBeFalse();
        var migrator = db.GetService<IMigrator>();
        const string current = "20261117192732_AddTripFlightLinks";
        const string previous = "20261020143618_AddTripsPlanWorkspace";
        var down = migrator.GenerateScript(current, previous);
        var up = migrator.GenerateScript(previous, current);
        static string Digest(string sql) =>
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql.ReplaceLineEndings("\n").Trim()))
            );
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"Executed SQL SHA256 (UTF-8 LF trimmed): down={Digest(down)} up={Digest(up)}"
        );
        await db.Database.ExecuteSqlRawAsync(down, Ct);
        (
            await db
                .Database.SqlQuery<bool>(
                    $"SELECT to_regclass('trips.trip_flight_links') IS NULL AS \"Value\""
                )
                .SingleAsync(Ct)
        ).ShouldBeTrue();
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBe([current]);
        (await planStore.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(saved);
        await db.Database.ExecuteSqlRawAsync(up, Ct);
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        (await links.LoadAsync(Owner, trip, Ct)).Value.ShouldBeNull();
        (await planStore.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(saved);
    }

    [Fact]
    public async Task Simultaneous_first_inserts_and_checked_noop_see_one_winner()
    {
        var beforeCommit = new CommitGate();
        await using var first = Db(beforeCommit);
        await using var second = Db();
        await using var noop = Db();
        var secondPid = await BackendPid(second);
        var noopPid = await BackendPid(noop);
        var write = new TripFlightLinkStore(first).CommitAsync(Binding(0), Record(1), Ct);
        await beforeCommit.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var competitor = new TripFlightLinkStore(second).CommitAsync(Binding(0), Record(1), Ct);
        var check = new TripFlightLinkStore(noop).CommitAsync(Binding(0), null, Ct);
        try
        {
            await WaitForLock(secondPid);
            await WaitForLock(noopPid);
        }
        finally
        {
            beforeCommit.Release.TrySetResult();
        }
        (await write).IsError.ShouldBeFalse();
        (await competitor).FirstError.ShouldBe(TripErrors.RevisionMismatch);
        (await check).FirstError.ShouldBe(TripErrors.RevisionMismatch);
        (
            await new TripFlightLinkStore(second).LoadAsync(Owner, trip, Ct)
        ).Value!.Binding.Revision.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Parent_delete_and_first_attach_follow_lock_order_without_orphans(
        bool attachFirst
    )
    {
        await using var waiting = Db();
        var waitingPid = await BackendPid(waiting);
        if (attachFirst)
        {
            var beforeCommit = new CommitGate();
            await using var attaching = Db(beforeCommit);
            var add = new TripFlightLinkStore(attaching).CommitAsync(Binding(0), Record(1), Ct);
            await beforeCommit.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var delete = new TripPlanStore(waiting).TryDeleteAsync(Owner, trip, 1, Ct);
            try
            {
                await WaitForLock(waitingPid);
            }
            finally
            {
                beforeCommit.Release.TrySetResult();
            }
            (await add).IsError.ShouldBeFalse();
            (await delete).IsError.ShouldBeFalse();
        }
        else
        {
            await using var deleting = Db();
            await using var tx = await deleting.Database.BeginTransactionAsync(Ct);
            (
                await new TripPlanStore(deleting).TryDeleteAsync(Owner, trip, 1, Ct)
            ).IsError.ShouldBeFalse();
            var add = new TripFlightLinkStore(waiting).CommitAsync(Binding(0), Record(1), Ct);
            await WaitForLock(waitingPid);
            await tx.CommitAsync(Ct);
            (await add).FirstError.ShouldBe(TripErrors.NotFound);
        }
        (await waiting.Set<TripFlightLinkEntity>().CountAsync(Ct)).ShouldBe(0);
        (await new TripFlightLinkStore(waiting).LoadAsync(Owner, trip, Ct)).FirstError.ShouldBe(
            TripErrors.NotFound
        );
    }

    [Fact]
    public async Task Protected_links_preserve_calendar_and_empty_revision_and_allow_deletion_after_damage()
    {
        using var ring = new TripsTestRing();
        await using var db = Db();
        var store = new TripFlightLinkStore(db);
        var codec = new TripFlightLinkCodec();
        var protector = new TripFlightLinkProtector(ring.Provider);
        var service = LocalService(db, ring);
        var planStore = new TripPlanStore(db);
        var plan = (await planStore.LoadAsync(Owner, trip, Ct)).Value!;
        var orderId = Guid.NewGuid();
        var links = (await service.GetAsync(Owner, trip, Ct))
            .Value.Change(true, orderId, DateTimeOffset.UtcNow)
            .Value;
        var record = new TripFlightLinkRecord(
            Binding(1),
            protector.Protect(codec.Serialize(links).Value, Binding(1)).Value
        );
        (await store.CommitAsync(Binding(0), record, Ct)).IsError.ShouldBeFalse();
        (await db.Set<TripFlightLinkEntity>().SingleAsync(Ct)).ProtectedPayload.ShouldNotContain(
            orderId.ToString()
        );
        (await service.GetAsync(Owner, trip, Ct)).Value.Links.Single().OrderId.ShouldBe(orderId);
        (await planStore.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(plan);
        var foreign = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        (await service.GetAsync(foreign, trip, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (
            await store.CommitAsync(Binding(1) with { Owner = foreign }, null, Ct)
        ).FirstError.ShouldBe(TripErrors.NotFound);
        (
            await planStore.TryReplaceAsync(1, plan with { Revision = 2 }, Ct)
        ).IsError.ShouldBeFalse();
        (await service.GetAsync(Owner, trip, Ct)).Value.Revision.ShouldBe(1);
        var empty = (await service.PatchAsync(Owner, trip, 1, false, orderId, Ct)).Value;
        empty.Revision.ShouldBe(2);
        empty.Links.ShouldBeEmpty();
        (await store.CommitAsync(Binding(0), null, Ct)).FirstError.ShouldBe(
            TripErrors.RevisionMismatch
        );
        await db.Set<TripFlightLinkEntity>()
            .ExecuteUpdateAsync(x => x.SetProperty(e => e.ProtectedPayload, "damaged"), Ct);
        (await service.GetAsync(Owner, trip, Ct)).FirstError.ShouldBe(TripErrors.Unavailable);
        (await planStore.TryDeleteAsync(Owner, trip, 2, Ct)).IsError.ShouldBeFalse();
        (await db.Set<TripFlightLinkEntity>().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Current_history_requires_real_table_and_every_mandatory_column_before_virtual_empty()
    {
        using var ring = new TripsTestRing();
        await using var db = Db();
        var services = new ServiceCollection().AddScoped<TripsDbContext>(_ => Db());
        using var container = services.BuildServiceProvider();
        var readiness = new TripsHealthCheck(
            new TripsReadiness(container.GetRequiredService<IServiceScopeFactory>(), ring.Provider)
        );
        var service = LocalService(db, ring);
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        (await readiness.CheckHealthAsync(new(), Ct)).Status.ShouldBe(HealthStatus.Healthy);
        (await service.GetAsync(Owner, trip, Ct)).Value.Revision.ShouldBe(0);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.trip_flight_links RENAME COLUMN protected_payload TO absent_payload",
            Ct
        );
        (await readiness.CheckHealthAsync(new(), Ct)).Status.ShouldBe(HealthStatus.Unhealthy);
        (await service.GetAsync(Owner, trip, Ct)).FirstError.ShouldBe(TripErrors.Unavailable);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.trip_flight_links RENAME COLUMN absent_payload TO protected_payload",
            Ct
        );
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.trip_flight_links RENAME TO absent_links",
            Ct
        );
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        (await readiness.CheckHealthAsync(new(), Ct)).Status.ShouldBe(HealthStatus.Unhealthy);
        (await service.GetAsync(Owner, trip, Ct)).FirstError.ShouldBe(TripErrors.Unavailable);
    }

    private static TripFlightLinkService LocalService(TripsDbContext db, TripsTestRing ring) =>
        new(
            new TripFlightLinkStore(db),
            new TripFlightLinkCodec(),
            new TripFlightLinkProtector(ring.Provider),
            () => throw new InvalidOperationException("Unexpected Flights lookup."),
            TimeProvider.System
        );

    private static async Task<int> BackendPid(TripsDbContext db)
    {
        await db.Database.OpenConnectionAsync(Ct);
        return await db
            .Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(Ct);
    }

    private async Task WaitForLock(int pid)
    {
        await using var db = Db();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (
            !await db
                .Database.SqlQuery<bool>(
                    $"SELECT cardinality(pg_blocking_pids({pid})) > 0 AS \"Value\""
                )
                .SingleAsync(deadline.Token)
        )
            await Task.Delay(20, deadline.Token);
    }

    private sealed class CommitGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Arrived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Arrived.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }
}
