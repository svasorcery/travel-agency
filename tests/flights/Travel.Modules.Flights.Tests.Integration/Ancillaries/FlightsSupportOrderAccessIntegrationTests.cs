using Marten;
using Shouldly;
using Travel.Modules.Flights.Application.Support;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

// Ordinary CI only: actual Marten source; this fixture deliberately has no EF read-model context.
[Trait("Category", "Integration")]
public sealed class FlightsSupportOrderAccessIntegrationTests(AncillaryMartenFixture fixture)
    : IClassFixture<AncillaryMartenFixture>
{
    private sealed class FixedClock(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    [Fact]
    public async Task Committed_held_order_is_verified_before_any_EF_projection_and_reads_add_no_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await fixture.Quote();
        Guid owner;
        await using (var session = fixture.Store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<BookingAggregate>(id, ct);
            owner = stream.Aggregate!.OwnerUserId!.Value;
            var quote = await new FlightsSupportOrderAccess(
                session,
                TimeProvider.System
            ).VerifyOwnershipAsync(owner, id, ct);
            quote.FirstError.Code.ShouldBe("Flights.OrderAccessNotFound");
            stream.AppendOne(
                new OfferHeldV2(
                    "ord_support_fixture",
                    ProtectedPassengerSnapshot.Create(1, "opaque-no-decrypt").Value,
                    AncillaryMartenFixture.Now.AddHours(1),
                    AncillaryMartenFixture.Now,
                    owner
                )
            );
            await session.SaveChangesAsync(ct);
        }
        await using (var restarted = fixture.Store.LightweightSession())
        {
            var facts = await new FlightsSupportTriageContext(
                restarted,
                new FixedClock(AncillaryMartenFixture.Now)
            ).ReadAsync(owner, id, ct);
            facts.IsError.ShouldBeFalse();
            facts.Value.OwnerId.ShouldBe(owner);
            facts.Value.OrderId.ShouldBe(id);
            facts.Value.StreamVersion.ShouldBe(3);
            facts.Value.Journey.ShouldBe(
                Travel.IntegrationContracts.Flights.FlightSupportJourneyState.Active
            );
            facts.Value.NextScheduledDepartureAt.ShouldBe(AncillaryMartenFixture.Now.AddDays(1));

            var access = new FlightsSupportOrderAccess(restarted, TimeProvider.System);
            var owned = await access.VerifyOwnershipAsync(owner, id, ct);
            owned.IsError.ShouldBeFalse();
            owned.Value.OrderId.ShouldBe(id);
            owned.Value.OwnerId.ShouldBe(owner);
            owned.Value.StreamVersion.ShouldBe(3);
            (await access.VerifyOwnershipAsync(Guid.NewGuid(), id, ct)).FirstError.Code.ShouldBe(
                "Flights.OrderAccessNotFound"
            );
            (await access.VerifyOwnershipAsync(owner, Guid.NewGuid(), ct)).FirstError.Code.ShouldBe(
                "Flights.OrderAccessNotFound"
            );
        }
        await using var observer = fixture.Store.QuerySession();
        (await observer.Events.FetchStreamAsync(id, token: ct)).Count.ShouldBe(3);
    }
}
