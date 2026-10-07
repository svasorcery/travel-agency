using ErrorOr;
using JasperFx.Events;
using Marten;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Booking;

internal static class BookingCreationWriter
{
    internal static BookableOffer Offer(
        BookingAggregate booking,
        BookingPurchase purchase,
        TimeProvider time
    ) =>
        new(
            booking.OfferId!.Value,
            booking.Itinerary!,
            purchase.BaseFare,
            ProviderId.Duffel,
            time.GetUtcNow(),
            purchase.ExpiresAt,
            booking.FareConditions ?? new FareConditions(false, false, null, null),
            booking.ProviderOfferRef!,
            booking.QuoteBinding!.Party
        );

    internal static async Task<ErrorOr<Success>> Observe(
        IEventStream<BookingAggregate> stream,
        IDocumentSession session,
        IMartenOutbox outbox,
        Guid attemptId,
        BookingCreationObservation observation,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var booking = stream.Aggregate!;
        if (booking.CurrentCreation is not { } attempt || attempt.Id != attemptId)
            return Error.Conflict("Flights.HoldOutcomeUnknown", "Creation state is unavailable.");
        var decision = booking.DecideCreationObservation(
            attemptId,
            attempt.Revision,
            observation,
            time.GetUtcNow()
        );
        if (decision.IsError)
            return decision.Errors;
        if (!decision.Value.Changed)
            return Result.Success;
        stream.AppendOne(decision.Value.Event!);
        var held =
            observation.Outcome
                is BookingCreationOutcome.Matches
                    or BookingCreationOutcome.CreatedWithDifferences
            && observation.Order is { }
            && booking.Status == BookingStatus.OfferQuoted
                ? new OfferHeldV3(
                    observation.Order.ProviderOrderId,
                    attempt.ProtectedParty,
                    observation.Order.PaymentRequiredBy,
                    time.GetUtcNow(),
                    attempt.OwnerId,
                    attempt.QuoteRevision,
                    booking.PassengerCount
                )
                : null;
        if (held is not null)
        {
            stream.AppendOne(held);
            stream.AppendOne(new BookingMutationCoordinationEnabled(time.GetUtcNow()));
        }
        await session.SaveBookingWithReconcileAsync(outbox, booking.Id, [], ct);
        booking.Apply(decision.Value.Event!);
        if (held is not null)
        {
            booking.Apply(held);
            booking.Apply(new BookingMutationCoordinationEnabled(time.GetUtcNow()));
        }
        return Result.Success;
    }
}
