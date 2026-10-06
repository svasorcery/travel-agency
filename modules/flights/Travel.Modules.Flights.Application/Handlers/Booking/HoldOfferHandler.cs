using System.Diagnostics;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class HoldOfferHandler
{
    // The explicit booking helper owns the commit and conflict translation.
    // Do not let generated middleware attempt a second save after a rejected write.
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<HeldOrderResult>> Handle(
        HoldOfferCommand cmd,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IDocumentSession marten,
        IMartenOutbox outbox,
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<HoldOfferCommand> log,
        IBookingPassengerPartyProtector protector,
        CancellationToken ct
    )
    {
        if (cmd.AggregateId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "HoldOfferCommand.AggregateId is required."
            );
        if (cmd.UserId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "HoldOfferCommand.UserId is required."
            );

        if (
            cmd.ProtectedPassengerParty is null
            || cmd.ProtectedPassengerParty.FormatVersion != 1
            || string.IsNullOrWhiteSpace(cmd.ProtectedPassengerParty.Ciphertext)
        )
            return PiiProtectionErrors.InvalidEnvelope;

        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["order_id"] = cmd.AggregateId,
                ["user_id"] = cmd.UserId,
                ["correlation_id"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            }
        );

        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        var ownerDecision = agg.DecideOwner(BookingTransition.Hold, cmd.UserId);
        if (ownerDecision is BookingTransitionDecision.Rejected ownerRejected)
            return BookingTransitionErrorMapper.ToOwnerError(ownerRejected.Reason, cmd.AggregateId);

        var transitionDecision = agg.DecideHold(
            time.GetUtcNow(),
            cmd.QuoteRevision,
            cmd.PassengerCount
        );
        if (transitionDecision is BookingTransitionDecision.Rejected transitionRejected)
            return BookingTransitionErrorMapper.ToError(transitionRejected.Reason);

        var context = new BookingPassengerPartyProtectionContext(
            cmd.AggregateId,
            cmd.UserId,
            cmd.QuoteRevision,
            cmd.PassengerCount
        );
        var passenger = protector.Unprotect(context, cmd.ProtectedPassengerParty);
        if (passenger.IsError)
            return passenger.Errors;

        var memberValidation = agg.QuoteBinding!.ValidatePassengers(
            passenger.Value,
            DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
        );
        if (memberValidation.IsError)
            return memberValidation.Errors;

        // One provider effect for the whole validated party.
        var provider = bookingProviders.Single();

        // FareConditions are captured at quote-time on the OfferQuoted event so the
        // hold request carries the exact terms shown to the user. Defensive fallback
        // covers any pre-WS2 stream that does not have the field on its OfferQuoted.
        var fareConditions = agg.FareConditions;
        if (fareConditions is null)
        {
            log.LogWarning(
                "BookingAggregate {AggregateId} predates FareConditions on OfferQuoted; using restrictive defaults.",
                cmd.AggregateId
            );
            fareConditions = new FareConditions(false, false, null, null);
        }
        var offer = new BookableOffer(
            Id: agg.OfferId!.Value,
            Itinerary: agg.Itinerary!,
            TotalAmount: agg.TotalAmount!,
            Provider: ProviderId.Duffel,
            FetchedAt: time.GetUtcNow(),
            ExpiresAt: agg.ExpiresAt!.Value,
            FareConditions: fareConditions,
            ProviderOfferRef: agg.ProviderOfferRef!,
            Party: agg.QuoteBinding!.Party
        );

        var held = await provider.HoldOfferAsync(offer, agg.QuoteBinding!, passenger.Value, ct);
        if (held.IsError)
            return held.FirstError;

        stream.AppendOne(
            new OfferHeldV3(
                OrderId: held.Value.ProviderOrderId,
                PassengerSnapshot: cmd.ProtectedPassengerParty,
                HeldUntil: held.Value.HeldUntil,
                HeldAt: time.GetUtcNow(),
                OwnerUserId: cmd.UserId,
                QuoteRevision: cmd.QuoteRevision,
                PassengerCount: cmd.PassengerCount
            )
        );

        stream.AppendOne(new BookingMutationCoordinationEnabled(time.GetUtcNow()));

        using var transitionSpan = FlightsActivitySource.Source.StartActivity(
            "booking.event.OfferHeldV3",
            ActivityKind.Internal
        );
        transitionSpan?.SetTag("aggregate.id", cmd.AggregateId.ToString());
        transitionSpan?.SetTag("aggregate.version", stream.CurrentVersion + 2);

        var saveResult = await marten.SaveOrConcurrencyConflictAsync(
            outbox,
            cmd.AggregateId,
            [],
            ct
        );
        if (saveResult.IsError)
            return saveResult.Errors;
        metrics.RecordAggregateEventsAppended(nameof(OfferHeldV3));
        metrics.RecordAggregateEventsAppended(nameof(BookingMutationCoordinationEnabled));

        return new HeldOrderResult(
            cmd.AggregateId,
            held.Value.ProviderOrderId,
            held.Value.HeldUntil
        );
    }
}
