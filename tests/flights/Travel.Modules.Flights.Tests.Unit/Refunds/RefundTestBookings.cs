using System.Reflection;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

internal static class RefundTestBookings
{
    internal static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
    internal static readonly Guid Booking = Guid.Parse("00000000-0000-0000-0000-000000000101");
    internal static readonly Guid Owner = Guid.Parse("00000000-0000-0000-0000-000000000102");
    internal static readonly Guid Cancellation = Guid.Parse("00000000-0000-0000-0000-000000000103");
    internal static readonly Guid Confirmation = Guid.Parse("00000000-0000-0000-0000-000000000104");
    internal static readonly Guid Sender = Guid.Parse("00000000-0000-0000-0000-000000000105");
    internal static readonly PaymentRef Payment = new(
        Guid.Parse("00000000-0000-0000-0000-000000000106")
    );
    internal const string Order = "ord_fictional_customer_refund";

    internal static Money Money(decimal amount, string currency = "GBP") =>
        Core.ValueObjects.Money.Create(amount, CurrencyCode.Create(currency).Value).Value;

    internal static BookingAggregate Held(int passengers = 1, int legs = 1)
    {
        var booking = new BookingAggregate();
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Id))!
            .SetValue(booking, Booking);
        var slices = Enumerable
            .Range(0, legs)
            .Select(i =>
                Slice
                    .Create([
                        Segment
                            .Create(
                                IataCode.Create(i % 2 == 0 ? "LHR" : "CDG").Value,
                                IataCode.Create(i % 2 == 0 ? "CDG" : "LHR").Value,
                                Now.AddDays(30 + i),
                                Now.AddDays(30 + i).AddHours(1),
                                "ZZ",
                                "ZZ101",
                                CabinClass.Parse("economy").Value
                            )
                            .Value,
                    ])
                    .Value
            )
            .ToArray();
        Apply(
            booking,
            new OfferQuoted(
                OfferId.New(),
                Itinerary.Create(slices).Value,
                Money(100m),
                Now.AddHours(1),
                "off_fictional_refund",
                Now
            )
        );
        Apply(
            booking,
            new OfferHeldV3(
                Order,
                ProtectedPassengerPartySnapshot.Create(1, "opaque-fictional-party").Value,
                Now.AddHours(1),
                Now,
                Owner,
                Guid.NewGuid(),
                passengers
            )
        );
        Apply(booking, new BookingMutationCoordinationEnabled(Now));
        return booking;
    }

    internal static BookingAggregate Cancelled(
        decimal captured = 100m,
        decimal refund = 80m,
        string refundCurrency = "GBP",
        bool captureObserved = true,
        CancellationResolutionSource source = CancellationResolutionSource.SupplierApi,
        int passengers = 1,
        int legs = 1
    )
    {
        var booking = Held(passengers, legs);
        var admission = Guid.NewGuid();
        Apply(
            booking,
            new ConfirmationAttemptStarted(
                Confirmation,
                Owner,
                admission,
                new string('c', 64),
                Order,
                Money(captured),
                Now
            )
        );
        Apply(booking, new ConfirmationEffectsClaimed(Confirmation, admission, Sender, Now));
        Apply(booking, new ConfirmationPaymentReferenceRecorded(Confirmation, Payment, Now));
        if (captureObserved)
            Apply(
                booking,
                new ConfirmationCaptureObserved(
                    Confirmation,
                    Payment,
                    Money(captured),
                    Now.AddSeconds(1)
                )
            );
        Apply(
            booking,
            new ConfirmationAttemptCompleted(
                Confirmation,
                Payment,
                Money(captured),
                Order,
                SupplierPaymentEvidence
                    .Create("pay_fictional_supplier", Money(captured), SupplierPaymentKind.Balance)
                    .Value,
                CancellationResolutionSource.SupplierApi,
                Now.AddSeconds(2)
            )
        );
        Apply(booking, new OrderConfirmed(Order, Payment, Now.AddSeconds(2)));
        Apply(
            booking,
            new CancellationPreparationStarted(
                Cancellation,
                Owner,
                Order,
                new string('a', 64),
                new string('p', 64),
                Guid.NewGuid(),
                Now.AddSeconds(3)
            )
        );
        var terms = CancellationTerms
            .Create(
                new(
                    1,
                    Booking,
                    Owner,
                    Cancellation,
                    Order,
                    "ore_fictional",
                    new string('a', 64),
                    Money(refund, refundCurrency),
                    CancellationRefundDestination.Balance,
                    new(SettlementComposition.CashOnly, true, source, false, false),
                    Now.AddMinutes(10),
                    "cancellation-v1"
                ),
                Now
            )
            .Value;
        Apply(booking, new CancellationTermsObtained(Cancellation, terms, Now.AddSeconds(3)));
        Apply(
            booking,
            new CancellationTermsAccepted(
                Cancellation,
                1,
                terms.Hash,
                terms.NoticeVersion,
                new string('d', 64),
                Guid.NewGuid(),
                Now.AddSeconds(3)
            )
        );
        Apply(
            booking,
            new CancellationConfirmationDispatched(
                Cancellation,
                Guid.NewGuid(),
                Sender,
                RecoverySchedule.Create(Guid.NewGuid(), Now.AddSeconds(3)).Value,
                Now.AddSeconds(3)
            )
        );
        var facts = new CancellationConfirmationFacts(
            Order,
            "ore_fictional",
            new string('a', 64),
            terms.Refund,
            terms.Destination,
            terms.Settlement,
            Now.AddSeconds(4),
            Now.AddSeconds(5),
            source
        );
        var evidence =
            source == CancellationResolutionSource.SupplierApi
                ? CancellationEvidence
                    .FromSupplierConfirmation(terms, facts, Now.AddSeconds(5))
                    .Value
                : CancellationEvidence
                    .FromOperatorConfirmation(terms, facts, Now.AddSeconds(5))
                    .Value;
        Apply(booking, new CancellationSucceeded(Cancellation, evidence, Now.AddSeconds(5)));
        Apply(booking, new OrderCancelled(CancelReason.User, Now.AddSeconds(4)));
        return booking;
    }

    internal static BookingAggregate Unpaid()
    {
        var b = Held();
        var revision = Guid.NewGuid();
        var purchase = Core
            .Booking.BookingPurchase.Create(revision, Owner, Money(100m), Now.AddHours(1), [])
            .Value;
        var attempt = Guid.NewGuid();
        Apply(
            b,
            new BookingCreationStarted(
                attempt,
                Owner,
                new string('a', 64),
                revision,
                purchase,
                ProtectedPassengerPartySnapshot.Create(1, "opaque-fictional-party").Value,
                Sender,
                Now
            )
        );
        var actual = new BookedOrderFacts(
            Order,
            "off_fictional_refund",
            b.Itinerary!,
            new([Guid.NewGuid()]),
            new([]),
            Money(100m),
            true,
            Now.AddHours(1)
        );
        Apply(
            b,
            new BookingCreationObserved(
                attempt,
                new(
                    Core.Booking.BookingCreationOutcome.CreatedWithDifferences,
                    actual,
                    Order,
                    true,
                    true,
                    "FictionalUnpaid",
                    Now
                ),
                Now
            )
        );
        Apply(
            b,
            new CancellationPreparationStarted(
                Cancellation,
                Owner,
                Order,
                new string('a', 64),
                new string('p', 64),
                Guid.NewGuid(),
                Now.AddSeconds(1)
            )
        );
        var terms = CancellationTerms
            .Create(
                new(
                    1,
                    Booking,
                    Owner,
                    Cancellation,
                    Order,
                    "ore_unpaid",
                    new string('a', 64),
                    Money(0m),
                    CancellationRefundDestination.AwaitingPayment,
                    new(
                        SettlementComposition.Unpaid,
                        true,
                        CancellationResolutionSource.SupplierApi,
                        false,
                        true
                    ),
                    Now.AddMinutes(10),
                    "cancellation-v1"
                ),
                Now
            )
            .Value;
        Apply(b, new CancellationTermsObtained(Cancellation, terms, Now.AddSeconds(2)));
        Apply(
            b,
            new CancellationTermsAccepted(
                Cancellation,
                1,
                terms.Hash,
                terms.NoticeVersion,
                new string('d', 64),
                Guid.NewGuid(),
                Now.AddSeconds(3)
            )
        );
        var facts = new CancellationConfirmationFacts(
            Order,
            "ore_unpaid",
            new string('a', 64),
            terms.Refund,
            terms.Destination,
            terms.Settlement,
            Now.AddSeconds(4),
            Now.AddSeconds(5),
            CancellationResolutionSource.SupplierApi
        );
        Apply(
            b,
            new CancellationSucceeded(
                Cancellation,
                CancellationEvidence
                    .FromSupplierConfirmation(terms, facts, Now.AddSeconds(5))
                    .Value,
                Now.AddSeconds(5)
            )
        );
        Apply(b, new OrderCancelled(CancelReason.User, Now.AddSeconds(4)));
        return b;
    }

    internal static void Apply(BookingAggregate booking, object e)
    {
        typeof(BookingAggregate).GetMethod("Apply", [e.GetType()])!.Invoke(booking, [e]);
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Version))!
            .SetValue(booking, booking.Version + 1);
    }
}
