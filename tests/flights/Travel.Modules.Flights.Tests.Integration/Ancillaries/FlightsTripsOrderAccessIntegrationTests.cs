using ErrorOr;
using Marten;
using Npgsql;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Flights.Application.Trips;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

[Trait("Category", "Integration")]
public sealed class FlightsTripsOrderAccessIntegrationTests(AncillaryMartenFixture fixture)
    : IClassFixture<AncillaryMartenFixture>
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Unprojected_owned_order_changes_at_the_next_source_version_and_reads_never_append()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = AncillaryMartenFixture.Now;
        var id = await fixture.Quote();
        Guid owner;
        await using (var write = fixture.Store.LightweightSession())
        {
            var stream = await write.Events.FetchForWriting<BookingAggregate>(id, ct);
            owner = stream.Aggregate!.OwnerUserId!.Value;
            (
                await new FlightsTripsOrderAccess(write, null!, new Clock(now)).ReadAsync(
                    owner,
                    id,
                    ct
                )
            ).FirstError.Type.ShouldBe(ErrorType.NotFound);
            stream.AppendOne(
                new OfferHeldV2(
                    "ord_fictional",
                    ProtectedPassengerSnapshot.Create(1, "opaque").Value,
                    now.AddHours(1),
                    now,
                    owner
                )
            );
            await write.SaveChangesAsync(ct);
        }
        await using (var read = fixture.Store.LightweightSession())
        {
            var port = new FlightsTripsOrderAccess(read, null!, new Clock(now));
            (await port.ReadAsync(Guid.NewGuid(), id, ct)).FirstError.Type.ShouldBe(
                ErrorType.NotFound
            );
            var observed = (await port.ReadAsync(owner, id, ct)).Value;
            observed.OrderId.ShouldBe(id);
            observed.OwnerId.ShouldBe(owner);
            observed.ObservedAt.ShouldBe(now);
            observed.BookingStatus.ShouldBe(FlightOrderBookingStatus.Held);
            observed.StreamVersion.ShouldBe(3);
            observed.Route!.Single().Origin.ShouldBe("LHR");
            read.PendingChanges.Streams().SelectMany(s => s.Events).ShouldBeEmpty();
            read.PendingChanges.Operations().ShouldBeEmpty();
        }
        await using (var write = fixture.Store.LightweightSession())
        {
            var stream = await write.Events.FetchForWriting<BookingAggregate>(id, ct);
            stream.AppendOne(new OrderCancelled(CancelReason.User, now.AddMinutes(1)));
            await write.SaveChangesAsync(ct);
        }
        await using (var read = fixture.Store.LightweightSession())
        {
            var port = new FlightsTripsOrderAccess(read, null!, new Clock(now.AddMinutes(2)));
            var observed = (await port.ReadAsync(owner, id, ct)).Value;
            observed.BookingStatus.ShouldBe(FlightOrderBookingStatus.Cancelled);
            observed.ReviewRequired.ShouldBeFalse();
            observed.ObservedAt.ShouldBe(now.AddMinutes(2));
            observed.StreamVersion.ShouldBe(4);
            read.PendingChanges.Streams().SelectMany(s => s.Events).ShouldBeEmpty();
            read.PendingChanges.Operations().ShouldBeEmpty();
        }
        await using var observer = fixture.Store.QuerySession();
        (await observer.Events.FetchStreamAsync(id, token: ct)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task Source_query_failure_is_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var session = fixture.Store.LightweightSession();
        await session.BeginTransactionAsync(ct);
        await using var fault = session.Connection.CreateCommand();
        fault.CommandText = "SELECT 1 / 0";
        await Should.ThrowAsync<PostgresException>(() => fault.ExecuteScalarAsync(ct));
        var port = new FlightsTripsOrderAccess(session, null!, TimeProvider.System);
        (await port.ReadAsync(Guid.NewGuid(), Guid.NewGuid(), ct)).FirstError.Type.ShouldBe(
            ErrorType.Failure
        );
    }
}
