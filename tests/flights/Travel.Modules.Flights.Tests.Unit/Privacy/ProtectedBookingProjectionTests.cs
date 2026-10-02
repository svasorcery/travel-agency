using System.Text.Json;
using JasperFx.Events;
using Marten.Events;
using Shouldly;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class ProtectedBookingProjectionTests
{
    [Fact]
    public void Projection_copies_protected_snapshot_without_decrypting()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var row = new OrderReadModelEntity
        {
            AggregateId = id,
            ProjectedStreamVersion = 1,
            ItineraryJson = "{}",
        };
        var held = new OfferHeldV2(
            "ord_fictional",
            ProtectedPassengerSnapshot.Create(1, "opaque-ciphertext").Value,
            DateTimeOffset.Parse("2030-01-01T01:00:00Z"),
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"),
            owner
        );
        var envelope = new Event<OfferHeldV2>(held) { StreamId = id, Version = 2 };
        OrderReadModelEventApplier.Apply(row, envelope);
        row.UserId.ShouldBe(owner);
        row.Status.ShouldBe("Held");
        row.PassengerInfoJson.ShouldBe(JsonSerializer.Serialize(held.PassengerSnapshot));
        row.ProjectedStreamVersion.ShouldBe(2);
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeTrue();
    }
}
