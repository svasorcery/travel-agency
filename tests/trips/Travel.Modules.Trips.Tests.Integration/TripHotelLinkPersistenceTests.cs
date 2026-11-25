using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Application.HotelLinks;
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
public sealed class TripHotelLinkPersistenceTests : IntegrationTestBase
{
    private const string PreviousMigration = "20261117192732_AddTripFlightLinks";
    private const string CurrentMigration = "20261124121853_AddTripHotelLinks";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static OwnerIdentity Owner => TripsCodecProtectionTests.Owner;
    private readonly TripsTestRing ring = new();
    private readonly Guid trip = Guid.NewGuid();
    private readonly Guid reservation = Guid.NewGuid();

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = Db();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct);
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var brief = TripsCodecProtectionTests.Brief;
        brief = brief with { EndDate = brief.StartDate.AddDays(1) };
        var blank = TripPlan.Create(trip, Owner, "Fictional trip", brief, now).Value;
        var plan = blank
            .Replace(
                blank.Title,
                brief,
                blank.Days.Select(day => new TripDayInput(day.Date, [])).ToArray(),
                now,
                [new TripStayInput("Fictional manual stay", brief.StartDate, brief.EndDate)]
            )
            .Value;
        var encoded = new TripPlanJsonCodec().Serialize(plan).Value;
        var binding = new TripPlanBinding(Owner, trip, plan.Revision);
        var envelope = (await ring.Protector.ProtectAsync(encoded, binding, Ct)).Value;
        (
            await new TripPlanStore(db).TryCreateAsync(
                new(trip, Owner, plan.Revision, now, now, envelope),
                100,
                Ct
            )
        ).IsError.ShouldBeFalse();
    }

    protected override ValueTask OnDisposingAsync()
    {
        ring.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Optional_migration_preserves_plan_and_flight_link_and_requires_its_own_valid_schema()
    {
        await using var db = Db();
        using var services = new ServiceCollection()
            .AddScoped<TripsDbContext>(_ => Db())
            .BuildServiceProvider();
        var trips = new TripsReadiness(
            services.GetRequiredService<IServiceScopeFactory>(),
            ring.Provider
        );
        var hotelLinks = new TripHotelLinkStore(db);
        var flightLinks = new TripFlightLinkStore(db);
        var planStore = new TripPlanStore(db);
        var plan = (await planStore.LoadAsync(Owner, trip, Ct)).Value!;
        TripFlightLinkRecord flight = new(new(Owner, trip, 1), "fictional-flight-envelope");
        (await flightLinks.CommitAsync(new(Owner, trip, 0), flight, Ct)).IsError.ShouldBeFalse();

        (await trips.IsReadyAsync(Ct)).ShouldBeTrue();
        (await hotelLinks.IsReadyAsync(Ct)).ShouldBeFalse();
        var migrator = db.GetService<IMigrator>();
        await db.Database.ExecuteSqlRawAsync(
            migrator.GenerateScript(PreviousMigration, CurrentMigration),
            Ct
        );
        (await hotelLinks.IsReadyAsync(Ct)).ShouldBeTrue();
        (await hotelLinks.LoadAsync(Owner, trip, Ct)).Value.ShouldBeNull();
        (
            await hotelLinks.CommitAsync(Binding(0), Record(1, reservation), Ct)
        ).IsError.ShouldBeFalse();
        (await planStore.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(plan);
        (await flightLinks.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(flight);

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.trip_hotel_links ALTER COLUMN protected_payload TYPE text",
            Ct
        );
        (await hotelLinks.IsReadyAsync(Ct)).ShouldBeFalse();
        (await trips.IsReadyAsync(Ct)).ShouldBeTrue();

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.trip_hotel_links ALTER COLUMN protected_payload TYPE character varying(512)",
            Ct
        );
        (await hotelLinks.IsReadyAsync(Ct)).ShouldBeTrue();
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.trip_hotel_links DROP CONSTRAINT fk_trip_hotel_links_trip_plans_trip_id",
            Ct
        );
        (await hotelLinks.IsReadyAsync(Ct)).ShouldBeFalse();
        (await trips.IsReadyAsync(Ct)).ShouldBeTrue();

        await db.Database.ExecuteSqlRawAsync(
            migrator.GenerateScript(CurrentMigration, PreviousMigration),
            Ct
        );
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBe([CurrentMigration]);
        (await hotelLinks.IsReadyAsync(Ct)).ShouldBeFalse();
        (await trips.IsReadyAsync(Ct)).ShouldBeTrue();
        (await planStore.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(plan);
        (await flightLinks.LoadAsync(Owner, trip, Ct)).Value.ShouldBe(flight);
        var plaintext = (
            await ring.Protector.UnprotectAsync(
                plan.ProtectedPayload,
                new(Owner, trip, plan.Revision),
                Ct
            )
        ).Value;
        new TripPlanJsonCodec()
            .Deserialize(plaintext.Plaintext, plan, plaintext.SchemaVersion)
            .Value.ManualStays.Single()
            .PlaceName.ShouldBe("Fictional manual stay");
    }

    [Fact]
    public async Task Owner_checked_slot_retains_empty_revision_and_damaged_protection_blocks_read()
    {
        await using var db = Db();
        await db.Database.MigrateAsync(Ct);
        var store = new TripHotelLinkStore(db);
        var protector = new TripHotelLinkProtector(ring.Provider);
        var stranger = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        (await store.LoadAsync(stranger, trip, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (await store.LoadAsync(Owner, Guid.NewGuid(), Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (
            await store.CommitAsync(Binding(0) with { Owner = stranger }, null, Ct)
        ).FirstError.ShouldBe(TripErrors.NotFound);
        (await store.CommitAsync(Binding(0), null, Ct)).IsError.ShouldBeFalse();
        (await store.LoadAsync(Owner, trip, Ct)).Value.ShouldBeNull();

        (await store.CommitAsync(Binding(0), Record(1, reservation), Ct)).IsError.ShouldBeFalse();
        await using var reopened = Db();
        var reopenedStore = new TripHotelLinkStore(reopened);
        var attached = (await reopenedStore.LoadAsync(Owner, trip, Ct)).Value!;
        attached.Binding.Revision.ShouldBe(1);
        attached.ProtectedPayload.ShouldNotContain(reservation.ToString());
        protector
            .Unprotect(attached.ProtectedPayload, attached.Binding)
            .Value.ShouldBe(reservation);

        (await reopenedStore.CommitAsync(Binding(1), Record(2, null), Ct)).IsError.ShouldBeFalse();
        var tombstone = (await store.LoadAsync(Owner, trip, Ct)).Value!;
        tombstone.Binding.Revision.ShouldBe(2);
        protector.Unprotect(tombstone.ProtectedPayload, tombstone.Binding).Value.ShouldBeNull();
        (await store.CommitAsync(Binding(0), Record(1, reservation), Ct)).FirstError.ShouldBe(
            TripErrors.RevisionMismatch
        );
        (await store.CommitAsync(Binding(2), null, Ct)).IsError.ShouldBeFalse();

        await db.HotelLinks.ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.ProtectedPayload, "damage"),
            Ct
        );
        var damaged = (await store.LoadAsync(Owner, trip, Ct)).Value!;
        protector
            .Unprotect(damaged.ProtectedPayload, damaged.Binding)
            .FirstError.ShouldBe(TripErrors.Unavailable);
    }

    [Fact]
    public async Task Competing_first_attach_and_no_op_observe_the_winning_revision()
    {
        var beforeCommit = new CommitGate();
        await using var second = Db();
        await second.Database.MigrateAsync(Ct);
        await using var first = Db(beforeCommit);
        await using var noop = Db();
        var secondPid = await BackendPid(second);
        var noopPid = await BackendPid(noop);
        var write = new TripHotelLinkStore(first).CommitAsync(
            Binding(0),
            Record(1, reservation),
            Ct
        );
        await beforeCommit.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var competing = new TripHotelLinkStore(second).CommitAsync(
            Binding(0),
            Record(1, Guid.NewGuid()),
            Ct
        );
        var check = new TripHotelLinkStore(noop).CommitAsync(Binding(0), null, Ct);
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
        (await competing).FirstError.ShouldBe(TripErrors.RevisionMismatch);
        (await check).FirstError.ShouldBe(TripErrors.RevisionMismatch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Attachment_and_plan_delete_follow_parent_lock_and_leave_no_orphan(
        bool attachFirst
    )
    {
        await using var waiting = Db();
        await waiting.Database.MigrateAsync(Ct);
        var planRevision = (await new TripPlanStore(waiting).LoadAsync(Owner, trip, Ct))
            .Value!
            .Revision;
        var waitingPid = await BackendPid(waiting);
        if (attachFirst)
        {
            var beforeCommit = new CommitGate();
            await using var attaching = Db(beforeCommit);
            var attach = new TripHotelLinkStore(attaching).CommitAsync(
                Binding(0),
                Record(1, reservation),
                Ct
            );
            await beforeCommit.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var delete = new TripPlanStore(waiting).TryDeleteAsync(Owner, trip, planRevision, Ct);
            try
            {
                await WaitForLock(waitingPid);
            }
            finally
            {
                beforeCommit.Release.TrySetResult();
            }
            (await attach).IsError.ShouldBeFalse();
            (await delete).IsError.ShouldBeFalse();
        }
        else
        {
            await using var deleting = Db();
            await using var tx = await deleting.Database.BeginTransactionAsync(Ct);
            (
                await new TripPlanStore(deleting).TryDeleteAsync(Owner, trip, planRevision, Ct)
            ).IsError.ShouldBeFalse();
            var attach = new TripHotelLinkStore(waiting).CommitAsync(
                Binding(0),
                Record(1, reservation),
                Ct
            );
            await WaitForLock(waitingPid);
            await tx.CommitAsync(Ct);
            (await attach).FirstError.ShouldBe(TripErrors.NotFound);
        }
        (await waiting.HotelLinks.CountAsync(Ct)).ShouldBe(0);
        (await new TripHotelLinkStore(waiting).LoadAsync(Owner, trip, Ct)).FirstError.ShouldBe(
            TripErrors.NotFound
        );
    }

    private TripHotelLinkBinding Binding(long revision) => new(Owner, trip, revision);

    private TripHotelLinkRecord Record(long revision, Guid? reservationId) =>
        new(
            Binding(revision),
            new TripHotelLinkProtector(ring.Provider)
                .Protect(reservationId, Binding(revision))
                .Value
        );

    private TripsDbContext Db(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(ConnectionString);
        TripsDbContextConfiguration.Configure(options);
        options.AddInterceptors(interceptors);
        return new(options.Options);
    }

    private static async Task<int> BackendPid(TripsDbContext db)
    {
        await db.Database.OpenConnectionAsync(Ct);
        return await db
            .Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(Ct);
    }

    private async Task WaitForLock(int pid)
    {
        await using var observer = Db();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (
            !await observer
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
