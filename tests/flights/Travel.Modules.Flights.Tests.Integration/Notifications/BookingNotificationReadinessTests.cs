using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Notifications;

[Trait("Category", "Integration")]
public sealed class BookingNotificationReadinessTests(BookingReconcilerFixture fixture)
    : IClassFixture<BookingReconcilerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missing_and_behind_rows_retry_until_projection_reaches_required_version()
    {
        var owner = Guid.NewGuid();
        var id = await fixture.SeedAsync(owner);
        var readiness = new BookingNotificationReadiness(fixture.Store, fixture.Options);
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() =>
            readiness.RequireAsync(id, owner, 2, Ct)
        );
        var reconciler = new OrderReadModelReconciler(
            fixture.Store,
            fixture.Options,
            new BookingProjectionMaintenanceContext()
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        (await readiness.RequireAsync(id, owner, 2, Ct)).ProjectedStreamVersion.ShouldBe(2);
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() =>
            readiness.RequireAsync(id, owner, 3, Ct)
        );
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() =>
            readiness.RequireAsync(id, Guid.NewGuid(), 2, Ct)
        );
    }

    [Fact]
    public async Task Old_envelope_requires_current_source_version_and_fresh_EF_snapshot()
    {
        var owner = Guid.NewGuid();
        var id = await fixture.SeedAsync(owner);
        var legacy = JsonSerializer.Deserialize<OrderConfirmedNotification>(
            $$"""{"AggregateId":"{{id}}","UserId":"{{owner}}"}"""
        )!;
        legacy.RequiredStreamVersion.ShouldBeNull();
        var readiness = new BookingNotificationReadiness(fixture.Store, fixture.Options);
        var reconciler = new OrderReadModelReconciler(
            fixture.Store,
            fixture.Options,
            new BookingProjectionMaintenanceContext()
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        (
            await readiness.RequireAsync(id, owner, legacy.RequiredStreamVersion, Ct)
        ).ProjectedStreamVersion.ShouldBe(2);
        await fixture.AppendAsync(
            id,
            new Travel.Modules.Flights.Core.DomainEvents.OrderCancelled(
                Travel.Modules.Flights.Core.DomainEvents.CancelReason.User,
                BookingReconcilerFixture.Now
            )
        );
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() =>
            readiness.RequireAsync(id, owner, legacy.RequiredStreamVersion, Ct)
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        (await readiness.RequireAsync(id, owner, null, Ct)).Status.ShouldBe("Cancelled");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Explicit_nonpositive_version_is_terminal(long version)
    {
        var readiness = new BookingNotificationReadiness(fixture.Store, fixture.Options);
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            readiness.RequireAsync(Guid.NewGuid(), Guid.NewGuid(), version, Ct)
        );
    }
}
