using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class MixedProjectionWriterCompatibilityTests(BookingReconcilerFixture fixture)
    : IClassFixture<BookingReconcilerFixture>
{
    [Fact]
    public async Task Old_writer_can_corrupt_status_without_advancing_checkpoint_and_requires_exclusive_repair()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await fixture.SeedAsync(Guid.NewGuid());
        await fixture.AppendAsync(
            id,
            new OrderCancelled(CancelReason.User, BookingReconcilerFixture.Now)
        );
        var reconciler = new OrderReadModelReconciler(
            fixture.Store,
            fixture.Options,
            new BookingProjectionMaintenanceContext()
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);
        await using var oldWriter = new FlightsDbContext(fixture.Options);
        // Test-local simulation of an old SQL writer that knows nothing about the new column.
        await oldWriter.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE flights.order_read_model SET status = 'Held' WHERE aggregate_id = {id}",
            ct
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);
        var row = await oldWriter.Orders.AsNoTracking().SingleAsync(x => x.AggregateId == id, ct);
        row.Status.ShouldBe("Held");
        row.ProjectedStreamVersion.ShouldBe(3);
        var validation = await reconciler.ValidateAsync(id, ct);
        validation.Issues.ShouldContain(x => x.Code == "DerivedFieldsMismatch");
    }
}
