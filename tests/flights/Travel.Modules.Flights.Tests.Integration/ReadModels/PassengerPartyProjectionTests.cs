using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Booking;

namespace Travel.Modules.Flights.Tests.Integration.ReadModels;

// CI-only: the existing fixture owns disposable PostgreSQL and applies checked-in migrations.
[Trait("Category", "Integration")]
public sealed class PassengerPartyProjectionTests(BookingReconcilerFixture fixture)
    : IClassFixture<BookingReconcilerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrderReadModelReconciler Service(bool reset = false) =>
        new(
            fixture.Store,
            fixture.Options,
            reset ? new ApprovedMaintenance() : new BookingProjectionMaintenanceContext()
        );

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public async Task V3_real_Marten_replay_prefix_suffix_and_equal_delivery_preserve_count_and_ciphertext(
        int count
    )
    {
        var seed = await SeedAsync(count);
        var initial = await Service()
            .ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct);
        initial.PersistedVersion.ShouldBe(2);
        await using (var session = fixture.Store.QuerySession())
        {
            var aggregate = await session.Events.AggregateStreamAsync<BookingAggregate>(
                seed.Id,
                token: Ct
            );
            aggregate.ShouldNotBeNull();
            aggregate.PassengerCount.ShouldBe(count);
            aggregate.ProtectedPassengerParty.ShouldBe(seed.Snapshot);
            aggregate.HeldQuoteRevision.ShouldBe(seed.Binding.Revision);
        }
        var payment = PaymentRef.New();
        await fixture.AppendAsync(
            seed.Id,
            new PaymentAuthorized(
                payment,
                BookingReconcilerFixture.Amount,
                BookingReconcilerFixture.Now
            ),
            new OrderConfirmed("ord_party", payment, BookingReconcilerFixture.Now)
        );
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() =>
            new BookingNotificationReadiness(fixture.Store, fixture.Options).RequireAsync(
                seed.Id,
                seed.Owner,
                4,
                Ct
            )
        );
        var suffix = await Service()
            .ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct);
        suffix.PreviousVersion.ShouldBe(2);
        suffix.AppliedEvents.ShouldBe(2);
        suffix.PersistedVersion.ShouldBe(4);
        (
            await Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct)
        ).AppliedEvents.ShouldBe(0);
        (await Service().ValidateAsync(seed.Id, Ct)).Issues.ShouldBeEmpty();
        await using var db = new FlightsDbContext(fixture.Options);
        var row = await db.Orders.AsNoTracking().SingleAsync(r => r.AggregateId == seed.Id, Ct);
        row.UserId.ShouldBe(seed.Owner);
        row.PassengerCount.ShouldBe(count);
        row.Status.ShouldBe("Confirmed");
        row.ProjectedStreamVersion.ShouldBe(4);
        JsonSerializer
            .Deserialize<ProtectedPassengerPartySnapshot>(row.PassengerInfoJson)
            .ShouldBe(seed.Snapshot);
        row.PassengerInfoJson.ShouldNotContain("givenName");
        var queries = new OrderReadModelQueries(db);
        (await queries.GetAsync(seed.Id, seed.Owner, Ct))!.PassengerCount.ShouldBe(count);
        (await queries.ListAsync(seed.Owner, 20, 0, Ct)).Items.ShouldContain(v =>
            v.AggregateId == seed.Id && v.PassengerCount == count && v.ProjectedStreamVersion == 4
        );
        (await queries.GetAsync(seed.Id, Guid.NewGuid(), Ct)).ShouldBeNull();
        var ready = await new BookingNotificationReadiness(
            fixture.Store,
            fixture.Options
        ).RequireAsync(seed.Id, seed.Owner, null, Ct);
        ready.PassengerCount.ShouldBe(count);
        ready.ProjectedStreamVersion.ShouldBe(4);
    }

    [Fact]
    public async Task Validate_reports_corrupted_count_equal_delivery_preserves_it_and_exclusive_reset_restores_source()
    {
        var seed = await SeedAsync(9);
        await Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct);
        Guid rowId;
        await using (var db = new FlightsDbContext(fixture.Options))
        {
            var row = await db.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct);
            rowId = row.Id;
            row.PassengerCount = 2;
            await db.SaveChangesAsync(Ct);
        }
        var validation = await Service().ValidateAsync(seed.Id, Ct);
        validation.Issues.ShouldContain(i =>
            i.Code == "DerivedFieldsMismatch" && i.RepairableByReset
        );
        (
            await Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct)
        ).AppliedEvents.ShouldBe(0);
        await using (var unchanged = new FlightsDbContext(fixture.Options))
            (
                await unchanged.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct)
            ).PassengerCount.ShouldBe(2);
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Reset, Ct)
        );
        await Service(true).ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Reset, Ct);
        await using var repaired = new FlightsDbContext(fixture.Options);
        var actual = await repaired.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct);
        actual.Id.ShouldBe(rowId);
        actual.PassengerCount.ShouldBe(9);
        actual.ProjectedStreamVersion.ShouldBe(2);
        JsonSerializer
            .Deserialize<ProtectedPassengerPartySnapshot>(actual.PassengerInfoJson)
            .ShouldBe(seed.Snapshot);
        (await Service().ValidateAsync(seed.Id, Ct)).Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V3_prefix_owner_is_verified_for_equal_version_and_payment_only_suffix(
        bool suffix
    )
    {
        var seed = await SeedAsync(2);
        await Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct);
        await using (var db = new FlightsDbContext(fixture.Options))
        {
            var row = await db.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct);
            row.UserId = Guid.NewGuid();
            await db.SaveChangesAsync(Ct);
        }
        if (suffix)
            await fixture.AppendAsync(
                seed.Id,
                new PaymentAuthorized(
                    PaymentRef.New(),
                    BookingReconcilerFixture.Amount,
                    BookingReconcilerFixture.Now
                )
            );
        var error = await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct)
        );
        error.Message.ShouldBe("ProjectionOwnerMismatch");
        await using var unchanged = new FlightsDbContext(fixture.Options);
        (
            await unchanged.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct)
        ).ProjectedStreamVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Maintenance_validate_and_reset_include_V3_count_without_keys()
    {
        var seed = await SeedAsync(9);
        await Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct);
        await using (var db = new FlightsDbContext(fixture.Options))
        {
            var row = await db.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct);
            row.PassengerCount = 1;
            await db.SaveChangesAsync(Ct);
        }
        var catalog = new SingleStream(seed.Id);
        var inspect = await new OrderReadModelRebuildRunner(catalog, Service()).RunAsync(
            false,
            false,
            Ct
        );
        inspect.Streams.ShouldContain(s =>
            s.AggregateId == seed.Id && s.Issues.Any(i => i.Code == "DerivedFieldsMismatch")
        );
        var rebuilt = await new OrderReadModelRebuildRunner(catalog, Service(true)).RunAsync(
            true,
            true,
            Ct
        );
        rebuilt.Failed.ShouldBe(0);
        await using var repaired = new FlightsDbContext(fixture.Options);
        (
            await repaired.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct)
        ).PassengerCount.ShouldBe(9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Persisted_count_bounds_are_enforced_by_source_migration(int count)
    {
        var seed = await SeedAsync(2);
        await Service().ReconcileAsync(seed.Id, OrderReadModelReconcileMode.Incremental, Ct);
        await using var db = new FlightsDbContext(fixture.Options);
        var row = await db.Orders.SingleAsync(r => r.AggregateId == seed.Id, Ct);
        row.PassengerCount = count;
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private async Task<Seed> SeedAsync(int count)
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var references = Enumerable
            .Range(1, count)
            .Select(i => SupplierPassengerReference.Create($"ref_party_{i}").Value)
            .ToArray();
        var party = BookableOfferParty
            .Create(
                references.Select(r => new SupplierPassengerSlot(r, BookingPassengerKind.Adult)),
                new(2026, 9, 23),
                true,
                false
            )
            .Value;
        var binding = QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                references.Select(r => new QuotePassengerSlot(
                    BookingPassengerId.Create(Guid.NewGuid()).Value,
                    r,
                    BookingPassengerKind.Adult
                ))
            )
            .Value;
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                BookingReconcilerFixture.Now.AddDays(1),
                BookingReconcilerFixture.Now.AddDays(1).AddHours(1),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var quote = new OfferQuoted(
            OfferId.New(),
            Itinerary.Create([Slice.Create([segment]).Value]).Value,
            BookingReconcilerFixture.Amount,
            BookingReconcilerFixture.Now.AddHours(1),
            "off_party",
            BookingReconcilerFixture.Now,
            QuoteBinding: binding
        );
        var snapshot = ProtectedPassengerPartySnapshot
            .Create(1, "opaque-party-envelope-no-key-service")
            .Value;
        var held = new OfferHeldV3(
            "ord_party",
            snapshot,
            BookingReconcilerFixture.Now.AddHours(2),
            BookingReconcilerFixture.Now,
            owner,
            binding.Revision,
            count
        );
        await using var session = fixture.Store.LightweightSession();
        session.Events.StartStream<BookingAggregate>(id, quote, held);
        await session.SaveChangesAsync(Ct);
        return new(id, owner, binding, snapshot);
    }

    private sealed record Seed(
        Guid Id,
        Guid Owner,
        QuoteBinding Binding,
        ProtectedPassengerPartySnapshot Snapshot
    );

    private sealed class ApprovedMaintenance : IBookingProjectionMaintenanceContext
    {
        public void RequireExclusiveReset() { }
    }

    private sealed class SingleStream(Guid id) : IBookingStreamCatalog
    {
        public async IAsyncEnumerable<Guid> ReadIdsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            yield return id;
            await Task.CompletedTask;
        }
    }
}
