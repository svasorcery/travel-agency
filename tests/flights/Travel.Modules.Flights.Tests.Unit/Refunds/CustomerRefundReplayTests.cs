using System.Reflection;
using System.Text.Json;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Shouldly;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundReplayTests
{
    public static IEnumerable<object[]> MetadataEvents()
    {
        var b = CustomerRefundDecisionTests.Admitted();
        var c = b.CustomerRefund!;
        IDomainEvent[] events =
        [
            new CustomerRefundRequested(c, RefundTestBookings.Now),
            new CustomerRefundDispatchClaimed(
                c.Id,
                RefundTestBookings.Sender,
                RefundTestBookings.Now
            ),
            new CustomerRefundOutcomeRecorded(c.Id, null, "Unknown", RefundTestBookings.Now),
            new CustomerRefundRequiredManualReview(c.Id, "OutcomeDeadline", RefundTestBookings.Now),
            new CustomerRefundWasNotRequired(
                c with
                {
                    State = CustomerRefundState.NoRefundDue,
                    Amount = RefundTestBookings.Money(0),
                },
                RefundTestBookings.Now
            ),
            new CustomerRefundReviewRecorded(
                c.Id,
                Guid.NewGuid(),
                RefundTestBookings.Owner,
                new string('a', 64),
                CustomerRefundReviewKind.Inconclusive,
                "evidence_fictional",
                null,
                null,
                RefundTestBookings.Now
            ),
            new SupplierCancellationReported(
                Guid.NewGuid(),
                RefundTestBookings.Owner,
                RefundTestBookings.Order,
                RefundTestBookings.Now
            ),
            new SupplierCancellationReportResolved(
                Guid.NewGuid(),
                SupplierCancellationReportState.ConfirmedCancellation,
                RefundTestBookings.Now,
                RefundTestBookings.Now
            ),
        ];
        return events.Select(e => new object[] { e });
    }

    [Theory]
    [MemberData(nameof(MetadataEvents))]
    public void Additive_events_round_trip_and_advance_only_checkpoint(IDomainEvent value)
    {
        var json = JsonSerializer.Serialize(value, value.GetType());
        JsonSerializer.Deserialize(json, value.GetType()).ShouldNotBeNull();
        var row = Row();
        var before = JsonSerializer.Serialize(row);
        OrderReadModelEventApplier.Apply(
            row,
            new Event<IDomainEvent>(value) { StreamId = row.AggregateId, Version = 3 }
        );
        row.ProjectedStreamVersion.ShouldBe(3);
        row.ProjectedStreamVersion = 2;
        JsonSerializer.Serialize(row).ShouldBe(before);
    }

    [Fact]
    public void New_event_types_are_registered_without_store_startup()
    {
        var options = new StoreOptions();
        typeof(OrderReadModelEventApplier)
            .Assembly.GetType(
                "Travel.Modules.Flights.Infrastructure.Marten.BookingAggregateConfig"
            )!
            .GetMethod("ConfigureFlightsBooking", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [options]);
        var known = ((EventGraph)options.Events)
            .AllKnownEventTypes()
            .Select(e => e.EventType)
            .ToHashSet();
        foreach (var values in MetadataEvents())
            known.ShouldContain(values[0].GetType());
    }

    [Fact]
    public void Ownerless_source_cannot_advance_on_refund_metadata()
    {
        var row = Row();
        row.UserId = null;
        var value = (IDomainEvent)MetadataEvents().First()[0];
        Should
            .Throw<BookingProjectionTerminalException>(() =>
                OrderReadModelEventApplier.Apply(
                    row,
                    new Event<IDomainEvent>(value) { StreamId = row.AggregateId, Version = 3 }
                )
            )
            .Message.ShouldBe("SourceOwnerMissing");
        row.ProjectedStreamVersion.ShouldBe(2);
    }

    private static OrderReadModelEntity Row() =>
        new()
        {
            AggregateId = RefundTestBookings.Booking,
            ProjectedStreamVersion = 2,
            UserId = RefundTestBookings.Owner,
            Status = "Ticketed",
            Currency = "GBP",
            TotalAmount = 100,
            ProviderOrderId = RefundTestBookings.Order,
            ItineraryJson = "fictional-preserved",
            PassengerInfoJson = "fictional-protected",
            PassengerCount = 9,
            TicketNumbers = ["fictional-ticket"],
            BookedAt = RefundTestBookings.Now,
            TicketedAt = RefundTestBookings.Now,
        };
}
