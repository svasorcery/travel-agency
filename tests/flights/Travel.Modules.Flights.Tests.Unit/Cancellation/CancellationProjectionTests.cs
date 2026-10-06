using System.Reflection;
using System.Text.Json;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Shouldly;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Unit.Aggregates;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationProjectionTests
{
    [Theory]
    [MemberData(
        nameof(CancellationReplayTests.MetadataEvents),
        MemberType = typeof(CancellationReplayTests)
    )]
    public void Metadata_events_advance_checkpoint_without_touching_displayed_order(
        IDomainEvent data
    )
    {
        var row = HeldRow();
        var before = JsonSerializer.Serialize(row);
        OrderReadModelEventApplier.Apply(
            row,
            new Event<IDomainEvent>(data) { StreamId = row.AggregateId, Version = 3 }
        );
        row.ProjectedStreamVersion.ShouldBe(3);
        row.ProjectedStreamVersion = 2;
        JsonSerializer.Serialize(row).ShouldBe(before);
    }

    [Fact]
    public void Unknown_event_fails_closed_without_advancing_checkpoint()
    {
        var row = HeldRow();
        Should
            .Throw<BookingProjectionTerminalException>(() =>
                OrderReadModelEventApplier.Apply(
                    row,
                    new Event<object>(new { Unexpected = "fictional" })
                    {
                        StreamId = row.AggregateId,
                        Version = 3,
                    }
                )
            )
            .Message.ShouldBe("SourceEventUnsupported");
        row.ProjectedStreamVersion.ShouldBe(2);
    }

    [Fact]
    public void New_metadata_without_owner_or_with_version_gap_cannot_project()
    {
        var row = HeldRow();
        row.UserId = null;
        Should
            .Throw<BookingProjectionTerminalException>(() =>
                OrderReadModelEventApplier.Apply(
                    row,
                    new Event<BookingMutationCoordinationEnabled>(new(CancellationTestData.Now))
                    {
                        StreamId = row.AggregateId,
                        Version = 3,
                    }
                )
            )
            .Message.ShouldBe("SourceOwnerMissing");
        row = HeldRow();
        Should
            .Throw<BookingProjectionTerminalException>(() =>
                OrderReadModelEventApplier.Apply(
                    row,
                    new Event<BookingMutationCoordinationEnabled>(new(CancellationTestData.Now))
                    {
                        StreamId = row.AggregateId,
                        Version = 4,
                    }
                )
            )
            .Message.ShouldBe("SourceVersionGap");
    }

    [Fact]
    public void All_new_events_are_registered_without_starting_a_store_or_app()
    {
        var options = new StoreOptions();
        typeof(OrderReadModelEventApplier)
            .Assembly.GetType(
                "Travel.Modules.Flights.Infrastructure.Marten.BookingAggregateConfig"
            )!
            .GetMethod("ConfigureFlightsBooking", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { options });
        var known = ((EventGraph)options.Events)
            .AllKnownEventTypes()
            .Select(e => e.EventType)
            .ToHashSet();
        foreach (var data in CancellationReplayTests.MetadataEvents())
            known.ShouldContain(data[0].GetType());
        known.ShouldContain(typeof(OfferHeld));
        known.ShouldContain(typeof(OfferHeldV2));
        known.ShouldContain(typeof(OfferHeldV3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Historical_quote_and_held_projection_stays_compatible(int version)
    {
        var row = new OrderReadModelEntity
        {
            AggregateId = Guid.NewGuid(),
            ProjectedStreamVersion = 0,
        };
        OrderReadModelEventApplier.Apply(
            row,
            new Event<OfferQuoted>(
                JsonSerializer.Deserialize<OfferQuoted>(MultiLegReplayTests.HistoricalQuoteJson)!
            )
            {
                StreamId = row.AggregateId,
                Version = 1,
            }
        );
        IDomainEvent held = version switch
        {
            1 => JsonSerializer.Deserialize<OfferHeld>(MultiLegReplayTests.HistoricalHeldV1Json)!,
            2 => JsonSerializer.Deserialize<OfferHeldV2>(MultiLegReplayTests.HistoricalHeldV2Json)!,
            _ => JsonSerializer.Deserialize<OfferHeldV3>(MultiLegReplayTests.HistoricalHeldV3Json)!,
        };
        OrderReadModelEventApplier.Apply(
            row,
            new Event<IDomainEvent>(held) { StreamId = row.AggregateId, Version = 2 }
        );
        row.PassengerCount.ShouldBe(version == 3 ? 2 : 1);
        row.ItineraryJson.ShouldContain("2030-06-01T08:00:00-04:00");
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeTrue();
    }

    private static OrderReadModelEntity HeldRow() =>
        new()
        {
            AggregateId = Guid.NewGuid(),
            ProjectedStreamVersion = 2,
            UserId = Guid.NewGuid(),
            Status = "Ticketed",
            Currency = "USD",
            TotalAmount = 100,
            ProviderOrderId = "ord_fictional",
            ItineraryJson = "fictional-preserved",
            PassengerInfoJson = "fictional-protected",
            PassengerCount = 2,
            TicketNumbers = ["fictional-ticket"],
            BookedAt = CancellationTestData.Now,
            TicketedAt = CancellationTestData.Now,
        };
}
