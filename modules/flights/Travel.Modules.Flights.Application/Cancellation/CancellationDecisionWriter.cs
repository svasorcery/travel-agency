using ErrorOr;
using JasperFx.Events;
using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Cancellation;

internal static class CancellationDecisionWriter
{
    internal static Error InvalidCommand =>
        Error.Validation("Flights.CancellationCommandInvalid", "Cancellation command invalid.");
    internal static Error NotFound =>
        Error.NotFound("Flights.BookingNotFound", "Booking not found.");

    internal static bool Owned(BookingAggregate? booking, Guid owner) =>
        owner != Guid.Empty && booking?.OwnerUserId == owner && booking.HasConsistentMutationOwner;

    internal static void RequireSource(BookingAggregate? booking)
    {
        if (booking is null)
            throw new InvalidOperationException("Cancellation source unavailable.");
        if (!booking.HasConsistentMutationOwner)
            throw new BookingSourceOwnershipMissingException("Cancellation source owner missing.");
    }

    internal static async Task<ErrorOr<CancellationStatusResult>> Command(
        CancellationDecision decision,
        IEventStream<BookingAggregate> stream,
        IDocumentSession session,
        IMartenOutbox outbox,
        IReadOnlyList<BookingWork> work,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (decision.Kind == CancellationDecisionKind.Rejected)
            return Reason(decision.Reason);
        var version = (stream.CurrentVersion ?? 0) + decision.Events.Count;
        try
        {
            await Persist(decision, stream, session, outbox, work, ct);
        }
        catch (BookingWriteConflictException)
        {
            return Error.Conflict("Flights.ConcurrencyConflict", "Booking changed concurrently.");
        }
        return CancellationStatusFactory.Create(
            stream.Aggregate!,
            version,
            decision.ResolvedOperationId,
            now
        );
    }

    internal static async Task Persist(
        CancellationDecision decision,
        IEventStream<BookingAggregate> stream,
        IDocumentSession session,
        IMartenOutbox outbox,
        IReadOnlyList<BookingWork> work,
        CancellationToken ct
    )
    {
        if (decision.Kind != CancellationDecisionKind.Allowed || decision.Events.Count == 0)
            return;
        var booking = stream.Aggregate!;
        var version = (stream.CurrentVersion ?? 0) + decision.Events.Count;
        foreach (var e in decision.Events)
            stream.AppendOne(e);
        var notifications = decision
            .Events.OfType<OrderCancelled>()
            .Select(e =>
                (object)
                    new OrderCancelledNotification(
                        booking.Id,
                        booking.OwnerUserId!.Value,
                        e.Reason,
                        version
                    )
            )
            .Concat(
                decision
                    .Events.OfType<OrderConfirmed>()
                    .Select(_ =>
                        (object)
                            new OrderConfirmedNotification(
                                booking.Id,
                                booking.OwnerUserId!.Value,
                                version
                            )
                    )
            )
            .ToArray();
        await session.SaveBookingWithWorkAsync(outbox, booking.Id, work, notifications, ct);
        foreach (var e in decision.Events)
            Apply(booking, e);
    }

    internal static Error Reason(CancellationReason reason) =>
        reason switch
        {
            CancellationReason.IdentityMismatch => NotFound,
            CancellationReason.InvalidResponse => InvalidCommand,
            _ => Error.Conflict(
                "Flights.Cancellation" + reason,
                "Cancellation action unavailable: " + reason + "."
            ),
        };

    private static void Apply(BookingAggregate booking, IDomainEvent e)
    {
        switch (e)
        {
            case BookingMutationCoordinationEnabled x:
                booking.Apply(x);
                break;
            case CancellationPreparationStarted x:
                booking.Apply(x);
                break;
            case CancellationPreparationDispatched x:
                booking.Apply(x);
                break;
            case CancellationTermsObtained x:
                booking.Apply(x);
                break;
            case CancellationTermsWereUnavailable x:
                booking.Apply(x);
                break;
            case CancellationTermsAccepted x:
                booking.Apply(x);
                break;
            case CancellationConfirmationDispatched x:
                booking.Apply(x);
                break;
            case CancellationObservationStarted x:
                booking.Apply(x);
                break;
            case CancellationObservationRecorded x:
                booking.Apply(x);
                break;
            case CancellationOutcomeBecameUnknown x:
                booking.Apply(x);
                break;
            case CancellationSucceeded x:
                booking.Apply(x);
                break;
            case CancellationRejected x:
                booking.Apply(x);
                break;
            case CancellationManualReviewRequired x:
                booking.Apply(x);
                break;
            case BookingOperationReviewRecorded x:
                booking.Apply(x);
                break;
            case CancellationReviewWasAbandoned x:
                booking.Apply(x);
                break;
            case CancellationReviewExpired x:
                booking.Apply(x);
                break;
            case CancellationRefreshRequested x:
                booking.Apply(x);
                break;
            case ConfirmationAttemptStarted x:
                booking.Apply(x);
                break;
            case ConfirmationEffectsClaimed x:
                booking.Apply(x);
                break;
            case ConfirmationPaymentReferenceRecorded x:
                booking.Apply(x);
                break;
            case ConfirmationCaptureObserved x:
                booking.Apply(x);
                break;
            case ConfirmationAttemptCompleted x:
                booking.Apply(x);
                break;
            case ConfirmationAttemptClosedWithoutEffects x:
                booking.Apply(x);
                break;
            case ConfirmationAttemptRequiredManualReview x:
                booking.Apply(x);
                break;
            case PaymentAuthorized x:
                booking.Apply(x);
                break;
            case OrderConfirmed x:
                booking.Apply(x);
                break;
            case OrderCancelled x:
                booking.Apply(x);
                break;
            default:
                throw new InvalidOperationException("Cancellation decision event unsupported.");
        }
    }
}
