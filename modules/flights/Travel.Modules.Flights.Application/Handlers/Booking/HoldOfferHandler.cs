using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class HoldOfferHandler
{
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
        CancellationToken ct,
        IDispatchInstanceIdentity? instance = null
    )
    {
        if (
            cmd.AggregateId == Guid.Empty
            || cmd.UserId == Guid.Empty
            || cmd.PassengerCount is < 1 or > 9
        )
            return Error.Validation("Flights.CommandInvalid", "Creation request is invalid.");
        if (
            cmd.ProtectedPassengerParty is null
            || cmd.ProtectedPassengerParty.FormatVersion != 1
            || string.IsNullOrWhiteSpace(cmd.ProtectedPassengerParty.Ciphertext)
        )
            return Error.Validation("Flights.PiiEnvelopeInvalid", "Protected party is invalid.");
        if (instance is null || instance.Id == Guid.Empty)
            return Error.Validation(
                "Flights.CreationRequestInvalid",
                "Creation sender identity is unavailable."
            );
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var booking = stream.Aggregate;
        if (booking is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());
        var now = time.GetUtcNow();
        var admission = booking.DecideCreationStart(
            cmd.UserId,
            cmd.RequestId,
            cmd.RequestDigest!,
            cmd.QuoteRevision,
            cmd.ProtectedPassengerParty,
            instance.Id,
            now,
            cmd.AcceptAncillaries
        );
        if (admission.IsError)
        {
            var guard = booking.DecideHold(now, cmd.QuoteRevision, cmd.PassengerCount);
            if (
                guard is BookingTransitionDecision.Rejected rejection
                && admission.FirstError.Code == "Flights." + rejection.Reason.Code
            )
                return BookingTransitionErrorMapper.ToError(rejection.Reason);
            return admission.Errors;
        }
        if (!admission.Value.IsNew)
            return Result(booking, admission.Value.AttemptId);
        if (cmd.PassengerCount != booking.PassengerCount)
            return Error.Conflict("Flights.PassengerCountMismatch", "Passenger count changed.");
        var party = protector.Unprotect(
            new(cmd.AggregateId, cmd.UserId, cmd.QuoteRevision, cmd.PassengerCount),
            cmd.ProtectedPassengerParty
        );
        if (party.IsError)
            return party.Errors;
        var valid = booking.QuoteBinding!.ValidatePassengers(
            party.Value,
            DateOnly.FromDateTime(now.UtcDateTime)
        );
        if (valid.IsError)
            return valid.Errors;
        var provider = bookingProviders.Single();
        var started = admission.Value.Event!;
        var offer = BookingCreationWriter.Offer(booking, started.Accepted, time);
        stream.AppendOne(started);
        try
        {
            await marten.SaveBookingWithWorkAsync(
                outbox,
                cmd.AggregateId,
                [
                    new(
                        new CheckBookingCreation(cmd.AggregateId, started.AttemptId),
                        now.AddSeconds(150)
                    ),
                ],
                [],
                ct
            );
        }
        catch (BookingWriteConflictException)
        {
            return FlightsErrors.ConcurrencyConflict;
        }
        booking.Apply(started);
        metrics.RecordAggregateEventsAppended(nameof(Core.DomainEvents.BookingCreationStarted));
        // Admission is durable. Browser cancellation can no longer cancel the one admitted supplier operation.
        var remaining = started.OccurredAt.AddSeconds(130) - time.GetUtcNow();
        BookingCreationObservation observation;
        if (remaining <= TimeSpan.Zero)
            observation = new(
                BookingCreationOutcome.ManualReviewRequired,
                null,
                null,
                false,
                true,
                "AdmissionDeadline",
                time.GetUtcNow()
            );
        else
        {
            using var operation = new CancellationTokenSource(remaining, time);
            try
            {
                var result = await provider.HoldOfferAsync(
                    offer,
                    booking.QuoteBinding!,
                    party.Value,
                    started.Accepted,
                    started.AttemptId,
                    operation.Token
                );
                observation = result.IsError ? Unknown(time) : result.Value;
            }
            catch (Exception)
            {
                observation = Unknown(time);
            }
        }
        using var saveBudget = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        await using var finalSession = marten.DocumentStore.LightweightSession();
        var current = await finalSession.Events.FetchForWriting<BookingAggregate>(
            cmd.AggregateId,
            saveBudget.Token
        );
        if (current.Aggregate is null)
            return FlightsErrors.HoldOutcomeUnknown;
        try
        {
            var saved = await BookingCreationWriter.Observe(
                current,
                finalSession,
                outbox,
                started.AttemptId,
                observation,
                time,
                saveBudget.Token
            );
            if (saved.IsError)
                return FlightsErrors.HoldOutcomeUnknown;
        }
        catch (BookingWriteConflictException)
        {
            return FlightsErrors.HoldOutcomeUnknown;
        }
        return Result(current.Aggregate, started.AttemptId);
    }

    private static BookingCreationObservation Unknown(TimeProvider time) =>
        new(
            BookingCreationOutcome.ManualReviewRequired,
            null,
            null,
            false,
            true,
            "OrderUnproven",
            time.GetUtcNow()
        );

    private static ErrorOr<HeldOrderResult> Result(BookingAggregate booking, Guid attemptId)
    {
        if (!booking.CreationAttempts.TryGetValue(attemptId, out var attempt))
            return FlightsErrors.HoldOutcomeUnknown;
        return attempt.Outcome switch
        {
            BookingCreationOutcome.Matches
                when attempt.Actual is { } actual
                    && booking.Status == BookingStatus.Held
                    && booking.CurrentCreationId == attemptId => new HeldOrderResult(
                booking.Id,
                actual.ProviderOrderId,
                actual.PaymentRequiredBy
            ),
            BookingCreationOutcome.CreatedWithDifferences => Error.Conflict(
                "Flights.HeldOrderNeedsCancellation",
                "The created order differs from the accepted purchase; cancellation is required."
            ),
            BookingCreationOutcome.NotCreated => Error.Conflict(
                "Flights.OrderNotCreated",
                "Order was not created; refresh before a new purchase."
            ),
            _ => FlightsErrors.HoldOutcomeUnknown,
        };
    }
}
