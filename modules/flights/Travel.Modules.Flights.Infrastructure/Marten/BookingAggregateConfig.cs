using JasperFx.Events.Projections;
using Marten;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;

namespace Travel.Modules.Flights.Infrastructure.Marten;

internal static class BookingAggregateConfig
{
    internal static void ConfigureFlightsBooking(this StoreOptions opts)
    {
        opts.Events.AddEventType(typeof(OfferQuoted));
        opts.Events.AddEventType(typeof(OfferReQuoted));
        opts.Events.AddEventType(typeof(OfferHeld));
        opts.Events.AddEventType(typeof(OfferHeldV2));
        opts.Events.AddEventType(typeof(OfferHeldV3));
        opts.Events.AddEventType(typeof(PaymentAuthorized));
        opts.Events.AddEventType(typeof(OrderConfirmed));
        opts.Events.AddEventType(typeof(OrderTicketed));
        opts.Events.AddEventType(typeof(OrderCancelled));
        opts.Events.AddEventType(typeof(OrderRefunded));
        opts.Events.AddEventType(typeof(BookingMutationCoordinationEnabled));
        opts.Events.AddEventType(typeof(CancellationPreparationStarted));
        opts.Events.AddEventType(typeof(CancellationPreparationDispatched));
        opts.Events.AddEventType(typeof(CancellationTermsObtained));
        opts.Events.AddEventType(typeof(CancellationTermsWereUnavailable));
        opts.Events.AddEventType(typeof(CancellationTermsAccepted));
        opts.Events.AddEventType(typeof(CancellationConfirmationDispatched));
        opts.Events.AddEventType(typeof(CancellationObservationStarted));
        opts.Events.AddEventType(typeof(CancellationObservationRecorded));
        opts.Events.AddEventType(typeof(CancellationOutcomeBecameUnknown));
        opts.Events.AddEventType(typeof(CancellationSucceeded));
        opts.Events.AddEventType(typeof(CancellationRejected));
        opts.Events.AddEventType(typeof(CancellationManualReviewRequired));
        opts.Events.AddEventType(typeof(BookingOperationReviewRecorded));
        opts.Events.AddEventType(typeof(CancellationReviewWasAbandoned));
        opts.Events.AddEventType(typeof(CancellationReviewExpired));
        opts.Events.AddEventType(typeof(CancellationRefreshRequested));
        opts.Events.AddEventType(typeof(ConfirmationAttemptStarted));
        opts.Events.AddEventType(typeof(ConfirmationEffectsClaimed));
        opts.Events.AddEventType(typeof(ConfirmationPaymentReferenceRecorded));
        opts.Events.AddEventType(typeof(ConfirmationCaptureObserved));
        opts.Events.AddEventType(typeof(ConfirmationAttemptCompleted));
        opts.Events.AddEventType(typeof(ConfirmationAttemptClosedWithoutEffects));
        opts.Events.AddEventType(typeof(ConfirmationAttemptRequiredManualReview));

        opts.Projections.Add(new BookingAggregateProjection(), ProjectionLifecycle.Live);
    }
}
