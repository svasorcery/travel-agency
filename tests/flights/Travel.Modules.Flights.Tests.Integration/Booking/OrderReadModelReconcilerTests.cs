using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Xunit;
using DocumentStore = Marten.DocumentStore;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class OrderReadModelReconcilerTests : IClassFixture<BookingReconcilerFixture>
{
    private readonly BookingReconcilerFixture _fixture;

    public OrderReadModelReconcilerTests(BookingReconcilerFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrderReadModelReconciler Service(bool maintenance = false) =>
        new(
            _fixture.Store,
            _fixture.Options,
            maintenance ? new ApprovedMaintenance() : new BookingProjectionMaintenanceContext()
        );

    [Fact]
    public async Task Quote_is_not_materialized_hold_materializes_and_payment_suffix_preserves_owner()
    {
        var id = await _fixture.SeedAsync(null);
        (
            await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        ).Materialized.ShouldBeFalse();
        var owner = Guid.NewGuid();
        await _fixture.AppendAsync(id, BookingReconcilerFixture.Held(owner));
        var initial = await Service()
            .ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        initial.PersistedVersion.ShouldBe(2);
        await _fixture.AppendAsync(
            id,
            new PaymentAuthorized(
                PaymentRef.New(),
                BookingReconcilerFixture.Amount,
                BookingReconcilerFixture.Now
            )
        );
        var suffix = await Service()
            .ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        suffix.AppliedEvents.ShouldBe(1);
        suffix.PreviousVersion.ShouldBe(2);
        suffix.PersistedVersion.ShouldBe(3);
        (
            await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        ).AppliedEvents.ShouldBe(0);
        await using var db = new FlightsDbContext(_fixture.Options);
        var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
        row.UserId.ShouldBe(owner);
        row.BookedAt.ShouldBe(BookingReconcilerFixture.Now);
    }

    [Fact]
    public async Task Missing_owner_is_unrepairable_and_missing_row_is_repairable()
    {
        var invalid = await _fixture.SeedAsync(null);
        await _fixture.AppendAsync(invalid, BookingReconcilerFixture.Held(null));
        var report = await Service().ValidateAsync(invalid, Ct);
        report.Issues.ShouldContain(x => x.Code == "SourceOwnerMissing" && !x.RepairableByReset);
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(invalid, OrderReadModelReconcileMode.Incremental, Ct)
        );
        var valid = await _fixture.SeedAsync(Guid.NewGuid());
        (await Service().ValidateAsync(valid, Ct)).Issues.ShouldContain(x =>
            x.Code == "ProjectionMissing" && x.RepairableByReset
        );
    }

    [Fact]
    public async Task Validate_is_read_only_and_reset_repairs_fields_owner_and_ahead_version()
    {
        var owner = Guid.NewGuid();
        var id = await _fixture.SeedAsync(owner);
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        Guid rowId;
        await using (var db = new FlightsDbContext(_fixture.Options))
        {
            var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            rowId = row.Id;
            row.UserId = Guid.NewGuid();
            row.TotalAmount = 999;
            row.ProjectedStreamVersion = 99;
            await db.SaveChangesAsync(Ct);
        }
        var report = await Service().ValidateAsync(id, Ct);
        report.Issues.ShouldContain(x => x.Code == "CheckpointAhead");
        report.Issues.ShouldContain(x => x.Code == "DerivedFieldsMismatch");
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct)
        );
        await using (var db = new FlightsDbContext(_fixture.Options))
            (
                await db.Orders.SingleAsync(x => x.AggregateId == id, Ct)
            ).ProjectedStreamVersion.ShouldBe(99);
        await Service(true).ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
        await using var verify = new FlightsDbContext(_fixture.Options);
        var repaired = await verify.Orders.SingleAsync(x => x.AggregateId == id, Ct);
        repaired.Id.ShouldBe(rowId);
        repaired.UserId.ShouldBe(owner);
        repaired.TotalAmount.ShouldBe(BookingReconcilerFixture.Amount.Amount);
        repaired.ProjectedStreamVersion.ShouldBe(2);
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_insert_or_update_retries_from_fresh_checkpoint(bool updating)
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        if (updating)
        {
            await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
            await _fixture.AppendAsync(
                id,
                new PaymentAuthorized(
                    PaymentRef.New(),
                    BookingReconcilerFixture.Amount,
                    BookingReconcilerFixture.Now
                )
            );
        }
        var barrier = new SaveBarrier();
        var options = new DbContextOptionsBuilder<FlightsDbContext>(_fixture.Options)
            .AddInterceptors(barrier)
            .Options;
        var service = new OrderReadModelReconciler(
            _fixture.Store,
            options,
            new BookingProjectionMaintenanceContext()
        );
        async Task<Exception?> Attempt()
        {
            try
            {
                await service.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }
        var failures = await Task.WhenAll(Attempt(), Attempt());
        failures.Count(x => x is null).ShouldBe(1);
        failures.Count(x => x is BookingProjectionTransientException).ShouldBe(1);
        var retry = await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        retry.AppliedEvents.ShouldBe(0);
        retry.PersistedVersion.ShouldBe(updating ? 3 : 2);
    }

    [Fact]
    public async Task Reset_removes_only_spurious_quote_row_and_failed_save_preserves_it()
    {
        var id = await _fixture.SeedAsync(null);
        var other = await _fixture.SeedAsync(Guid.NewGuid());
        await Service().ReconcileAsync(other, OrderReadModelReconcileMode.Incremental, Ct);
        await using (var db = new FlightsDbContext(_fixture.Options))
        {
            db.Orders.Add(
                new OrderReadModelEntity
                {
                    Id = Guid.NewGuid(),
                    AggregateId = id,
                    UserId = Guid.NewGuid(),
                    Status = "Held",
                    Currency = "USD",
                    ItineraryJson = "{}",
                    PassengerInfoJson = "{}",
                    ProviderOrderId = "wrong",
                    BookedAt = BookingReconcilerFixture.Now,
                    ProjectedStreamVersion = 1,
                }
            );
            await db.SaveChangesAsync(Ct);
        }
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldContain(x =>
            x.Code == "ProjectionUnexpected" && x.RepairableByReset
        );
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        var options = new DbContextOptionsBuilder<FlightsDbContext>(_fixture.Options)
            .AddInterceptors(new FailingSave())
            .Options;
        var failing = new OrderReadModelReconciler(
            _fixture.Store,
            options,
            new ApprovedMaintenance()
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            failing.ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct)
        );
        await using (var db = new FlightsDbContext(_fixture.Options))
            (await db.Orders.CountAsync(x => x.AggregateId == id, Ct)).ShouldBe(1);
        await Service(true).ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
        await using var verify = new FlightsDbContext(_fixture.Options);
        (await verify.Orders.CountAsync(x => x.AggregateId == id, Ct)).ShouldBe(0);
        (await verify.Orders.CountAsync(x => x.AggregateId == other, Ct)).ShouldBe(1);
        await using var source = _fixture.Store.QuerySession();
        (await source.Events.FetchStreamStateAsync(id, Ct))!.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Corrupt_owner_cannot_be_preserved_by_incremental_suffix()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        await using (var db = new FlightsDbContext(_fixture.Options))
        {
            var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            row.UserId = Guid.NewGuid();
            await db.SaveChangesAsync(Ct);
        }
        await _fixture.AppendAsync(
            id,
            new PaymentAuthorized(
                PaymentRef.New(),
                BookingReconcilerFixture.Amount,
                BookingReconcilerFixture.Now
            )
        );
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        await Service(true).ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Captured_target_excludes_new_events_committed_before_EF_save()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        var interceptor = new AppendAtSave(() =>
            _fixture.AppendAsync(
                id,
                new PaymentAuthorized(
                    PaymentRef.New(),
                    BookingReconcilerFixture.Amount,
                    BookingReconcilerFixture.Now
                )
            )
        );
        var options = new DbContextOptionsBuilder<FlightsDbContext>(_fixture.Options)
            .AddInterceptors(interceptor)
            .Options;
        var first = await new OrderReadModelReconciler(
            _fixture.Store,
            options,
            new BookingProjectionMaintenanceContext()
        ).ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        first.SourceVersion.ShouldBe(2);
        first.PersistedVersion.ShouldBe(2);
        var next = await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        next.AppliedEvents.ShouldBe(1);
        next.PersistedVersion.ShouldBe(3);
    }

    [Fact]
    public async Task Bootstrap_and_unknown_source_fail_without_advancing_checkpoint()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        await using (var db = new FlightsDbContext(_fixture.Options))
        {
            var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            row.ProjectedStreamVersion = -1;
            await db.SaveChangesAsync(Ct);
        }
        var error = await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        error.Message.ShouldBe("ProjectionBootstrapRequired");
        await Service(true).ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
        await _fixture.AppendAsync(id, new UnsupportedBookingEvent("test"));
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldContain(x =>
            x.Code == "SourceEventUnsupported" && !x.RepairableByReset
        );
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        await using var verify = new FlightsDbContext(_fixture.Options);
        (
            await verify.Orders.SingleAsync(x => x.AggregateId == id, Ct)
        ).ProjectedStreamVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Terminal_history_maps_event_timestamps_and_same_version_reset_repairs_corruption()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        var now = BookingReconcilerFixture.Now;
        await _fixture.AppendAsync(
            id,
            new OrderConfirmed("ord-confirmed", PaymentRef.New(), now.AddMinutes(1)),
            new OrderTicketed(new[] { "TKT-1" }, now.AddMinutes(2)),
            new OrderRefunded(
                RefundRef.New(),
                BookingReconcilerFixture.Amount,
                RefundInitiator.Airline,
                now.AddMinutes(3)
            )
        );
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        await using var tracked = new FlightsDbContext(_fixture.Options);
        var row = await tracked.Orders.SingleAsync(x => x.AggregateId == id, Ct);
        row.Status.ShouldBe("Refunded");
        row.TicketNumbers.ShouldBe(["TKT-1"]);
        row.TicketedAt.ShouldBe(now.AddMinutes(2));
        row.RefundedAt.ShouldBe(now.AddMinutes(3));
        row.TotalAmount = 999;
        await tracked.SaveChangesAsync(Ct); // Deliberate same-version derived-row corruption.
        row.Currency = "WRONG"; // Unsaved caller tracking must not contaminate Validate/Reset.
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldContain(x =>
            x.Code == "DerivedFieldsMismatch"
        );
        await Service(true).ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldBeEmpty();
        await using var verify = new FlightsDbContext(_fixture.Options);
        var repaired = await verify.Orders.SingleAsync(x => x.AggregateId == id, Ct);
        repaired.ProjectedStreamVersion.ShouldBe(5);
        repaired.Currency.ShouldBe("USD");
        repaired.TotalAmount.ShouldBe(100);
    }

    [Fact]
    public async Task Failed_catchup_preserves_previous_version_and_fresh_attempt_converges()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        await _fixture.AppendAsync(
            id,
            new OrderCancelled(CancelReason.User, BookingReconcilerFixture.Now.AddMinutes(1))
        );
        var options = new DbContextOptionsBuilder<FlightsDbContext>(_fixture.Options)
            .AddInterceptors(new FailingSave())
            .Options;
        var failing = new OrderReadModelReconciler(
            _fixture.Store,
            options,
            new BookingProjectionMaintenanceContext()
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            failing.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        await using (var verify = new FlightsDbContext(_fixture.Options))
        {
            var row = await verify.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            row.ProjectedStreamVersion.ShouldBe(2);
            row.Status.ShouldBe("Held");
        }
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_source_and_wrong_stream_type_are_not_deletion_evidence()
    {
        var missing = await Service().ValidateAsync(Guid.NewGuid(), Ct);
        missing.Issues.ShouldContain(x => x.Code == "SourceMissing" && !x.RepairableByReset);
        var id = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession())
        {
            session.Events.StartStream<UnsupportedBookingEvent>(
                id,
                new UnsupportedBookingEvent("not-booking")
            );
            await session.SaveChangesAsync(Ct);
        }
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldContain(x =>
            x.Code == "SourceStreamTypeInvalid" && !x.RepairableByReset
        );
    }

    [Fact]
    public async Task Gapped_source_cannot_advance_the_checkpoint()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        await _fixture.AppendAsync(
            id,
            new PaymentAuthorized(
                PaymentRef.New(),
                BookingReconcilerFixture.Amount,
                BookingReconcilerFixture.Now
            ),
            new OrderConfirmed("ord-test", PaymentRef.New(), BookingReconcilerFixture.Now)
        );
        await using var db = new FlightsDbContext(_fixture.Options);
        // Fault injection only in this test-owned disposable database; keep mt_streams at version 4.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM public.mt_events WHERE stream_id = {id} AND version = 3",
            Ct
        );
        var failure = await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        failure.Message.ShouldBe("SourceVersionGap");
        (
            await db.Orders.AsNoTracking().SingleAsync(x => x.AggregateId == id, Ct)
        ).ProjectedStreamVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requote_snapshot_must_match_its_event_identity_and_price(bool inconsistent)
    {
        var id = await _fixture.SeedAsync(null);
        OfferQuoted quote;
        await using (var session = _fixture.Store.QuerySession())
            quote = (OfferQuoted)(await session.Events.FetchStreamAsync(id, token: Ct))[0].Data;
        var refreshed = new BookableOffer(
            OfferId.New(),
            quote.Itinerary,
            Money.Create(150, quote.TotalAmount.Currency).Value,
            ProviderId.Duffel,
            BookingReconcilerFixture.Now,
            BookingReconcilerFixture.Now.AddHours(1),
            new FareConditions(false, false, null, null),
            quote.ProviderRef
        );
        await _fixture.AppendAsync(
            id,
            new OfferReQuoted(
                inconsistent ? quote.OfferId : refreshed.Id,
                quote.TotalAmount,
                refreshed.TotalAmount,
                BookingReconcilerFixture.Now,
                refreshed
            ),
            BookingReconcilerFixture.Held(Guid.NewGuid())
        );
        if (inconsistent)
        {
            (await Service().ValidateAsync(id, Ct)).Issues.ShouldContain(x =>
                x.Code == "SourcePayloadInvalid" && !x.RepairableByReset
            );
            await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
                Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct)
            );
        }
        else
        {
            await Service().ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
            await using var db = new FlightsDbContext(_fixture.Options);
            (await db.Orders.SingleAsync(x => x.AggregateId == id, Ct)).TotalAmount.ShouldBe(150);
        }
    }

    [Fact]
    public async Task Payment_without_a_source_owner_is_not_a_valid_quote_only_stream()
    {
        var id = await _fixture.SeedAsync(null);
        await _fixture.AppendAsync(
            id,
            new PaymentAuthorized(
                PaymentRef.New(),
                BookingReconcilerFixture.Amount,
                BookingReconcilerFixture.Now
            )
        );
        (await Service().ValidateAsync(id, Ct)).Issues.ShouldContain(x =>
            x.Code == "SourceOwnerMissing" && !x.RepairableByReset
        );
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _count;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref _count) == 2)
                _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Injected save failure");
    }

    private sealed class AppendAtSave(Func<Task> append) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            await append();
            return result;
        }
    }

    private sealed class ApprovedMaintenance : IBookingProjectionMaintenanceContext
    {
        public void RequireExclusiveReset() { }
    }
}

public sealed record UnsupportedBookingEvent(string Reason);

public sealed class BookingReconcilerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    public DocumentStore Store { get; private set; } = default!;
    public DbContextOptions<FlightsDbContext> Options { get; private set; } = default!;
    public static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    public static readonly Money Amount = Money.Create(100, CurrencyCode.Create("USD").Value).Value;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Options = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                x => x.MigrationsHistoryTable("__ef_migrations_history", "flights")
            )
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new FlightsDbContext(Options);
        await db.Database.MigrateAsync();
        Store = DocumentStore.For(options =>
        {
            options.Connection(_postgres.GetConnectionString());
            options.AutoCreateSchemaObjects = AutoCreate.All;
            FlightsModule.ConfigureMarten(options);
        });
    }

    public async ValueTask DisposeAsync()
    {
        Store.Dispose();
        await _postgres.DisposeAsync();
    }

    public async Task<Guid> SeedAsync(Guid? owner)
    {
        var id = Guid.NewGuid();
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var quote = new OfferQuoted(
            OfferId.New(),
            Itinerary.Create([Slice.Create([segment]).Value]).Value,
            Amount,
            Now.AddHours(1),
            "off-test",
            Now
        );
        await using var session = Store.LightweightSession();
        session.Events.StartStream<BookingAggregate>(id, quote);
        if (owner is not null)
            session.Events.Append(id, Held(owner));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    public async Task AppendAsync(Guid id, params object[] events)
    {
        await using var session = Store.LightweightSession();
        session.Events.Append(id, events);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static OfferHeld Held(Guid? owner) =>
        new(
            "ord-test",
            PassengerInfo
                .Create(
                    "Ivan",
                    "Petrov",
                    new DateOnly(1990, 1, 1),
                    Gender.Male,
                    "ivan@example.test",
                    PhoneNumber.Create("+79161234567").Value,
                    new DateOnly(2026, 9, 22)
                )
                .Value,
            Now.AddHours(2),
            Now,
            owner
        );
}
