using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Trips;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Shared.TestInfrastructure;

namespace Travel.Modules.Flights.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class TripsCandidateProjectionTests : IntegrationTestBase
{
    [Fact]
    public async Task Owner_and_materialization_filters_precede_ordering_and_lookahead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = FlightsDbContextOptionsParityTests.CreateDesignTimeContext();
        db.Database.SetConnectionString(ConnectionString);
        await db.Database.MigrateAsync(ct);
        var owner = Guid.NewGuid();
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        OrderReadModelEntity Row(Guid userId, string status, DateTimeOffset at) =>
            new()
            {
                Id = Guid.NewGuid(),
                AggregateId = Guid.NewGuid(),
                UserId = userId,
                Status = status,
                BookedAt = at,
                Currency = "GBP",
                ItineraryJson = "{}",
                PassengerInfoJson = "{}",
            };
        var own = Enumerable
            .Range(0, 21)
            .Select(i => Row(owner, "Held", now.AddHours(i / 2)))
            .ToArray();
        own[0].ItineraryJson = System.Text.Json.JsonSerializer.Serialize(
            new { data = new string('x', ProjectedFlightOrderCandidate.MaximumItineraryBytes) }
        );
        db.Orders.AddRange(own);
        db.Orders.AddRange(
            Row(Guid.NewGuid(), "Held", now.AddDays(1)),
            Row(owner, "OfferQuoted", now.AddDays(1))
        );
        await db.SaveChangesAsync(ct);
        var queries = new OrderReadModelQueries(db);
        var projected = await queries.ListCandidatesAsync(owner, 21, 0, ct);
        projected.Single(r => r.OrderId == own[0].AggregateId).ItineraryJson.ShouldBe("null");
        var port = new FlightsTripsOrderAccess(null!, queries, TimeProvider.System);
        var first = (await port.ListCandidatesAsync(owner, 0, ct)).Value;
        var last = (await port.ListCandidatesAsync(owner, first.NextOffset!.Value, ct)).Value;
        var ordered = own.OrderByDescending(o => o.BookedAt)
            .ThenByDescending(o => o.AggregateId)
            .Select(o => o.AggregateId);
        first.Items.Concat(last.Items).Select(o => o.OrderId).ShouldBe(ordered);
        first.Items.ShouldAllBe(o => o.Route == null);
        last.NextOffset.ShouldBeNull();
    }
}
