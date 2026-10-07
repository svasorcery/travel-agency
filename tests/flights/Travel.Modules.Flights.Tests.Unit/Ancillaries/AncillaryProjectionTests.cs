using JasperFx.Events;
using Marten.Events;
using Shouldly;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class AncillaryProjectionTests
{
    [Fact]
    public void Purchase_and_admission_advance_without_partial_EF_row_and_actual_price_applies_once()
    {
        var (booking, actual, now) = AncillaryConfirmationTests.Held();
        var attempt = booking.CurrentCreation!;
        var row = new OrderReadModelEntity { AggregateId = booking.Id, ProjectedStreamVersion = 0 };
        long version = 0;
        void Apply<T>(T data)
            where T : notnull =>
            OrderReadModelEventApplier.Apply(
                row,
                new Event<T>(data) { StreamId = booking.Id, Version = ++version }
            );
        Apply(
            new OfferQuoted(
                booking.OfferId!.Value,
                booking.Itinerary!,
                attempt.Accepted.BaseFare,
                attempt.Accepted.ExpiresAt,
                booking.ProviderOfferRef!,
                now,
                booking.FareConditions,
                booking.QuoteBinding
            )
        );
        Apply(new BookingPurchaseQuoted(attempt.Accepted, now));
        row.TotalAmount.ShouldBe(70);
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeFalse();
        Apply(
            new BookingCreationStarted(
                attempt.Id,
                attempt.OwnerId,
                attempt.Digest,
                attempt.QuoteRevision,
                attempt.Accepted,
                attempt.ProtectedParty,
                attempt.SenderInstanceId,
                attempt.StartedAt
            )
        );
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeFalse();
        var different = actual with
        {
            Total = Money.Create(50, actual.Total.Currency).Value,
            Services = new([]),
        };
        Apply(
            new BookingCreationObserved(
                attempt.Id,
                new(
                    BookingCreationOutcome.CreatedWithDifferences,
                    different,
                    actual.ProviderOrderId,
                    true,
                    true,
                    "OrderObserved",
                    now
                ),
                now
            )
        );
        Apply(
            new OfferHeldV3(
                actual.ProviderOrderId,
                attempt.ProtectedParty,
                actual.PaymentRequiredBy,
                now,
                attempt.OwnerId,
                attempt.QuoteRevision,
                2
            )
        );
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeTrue();
        row.TotalAmount.ShouldBe(50);
        row.PassengerCount.ShouldBe(2);
        row.UserId.ShouldBe(attempt.OwnerId);
        row.ProjectedStreamVersion.ShouldBe(5);
        row.PassengerInfoJson.ShouldNotContain("family_name");
    }
}
