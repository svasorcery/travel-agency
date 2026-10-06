using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class ConfirmOrderHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<ConfirmedOrderResult>> Handle(
        ConfirmOrderCommand cmd,
        IDocumentSession marten,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IPaymentGateway payments,
        IFlightsMetrics metrics,
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger<ConfirmOrderCommand> log,
        IDispatchInstanceIdentity instance,
        CancellationToken ct
    )
    {
        if (cmd.AggregateId == Guid.Empty || cmd.UserId == Guid.Empty)
            return Error.Validation("Flights.CommandInvalid", "Confirmation command invalid.");
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var booking = stream.Aggregate;
        if (!CancellationDecisionWriter.Owned(booking, cmd.UserId))
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());
        if (
            booking!.CurrentConfirmationAttempt
                is { Phase: ConfirmationAttemptPhase.Completed } completed
            && completed.PaymentReference is { } known
        )
            return new ConfirmedOrderResult(
                booking.Id,
                booking.Status.ToString(),
                known.Value.ToString("N")
            );
        if (booking.HasConfirmationBarrier)
            return FlightsErrors.ConfirmationOutcomeUnknown;
        if (booking.Status == BookingStatus.Held && !booking.MutationCoordinationEnabled)
            return Error.Conflict(
                "Flights.LegacyConfirmationUnverified",
                "Legacy confirmation requires operator verification."
            );
        var transition = booking.DecideConfirm(time.GetUtcNow());
        if (transition is BookingTransitionDecision.Rejected rejection)
            return BookingTransitionErrorMapper.ToError(rejection.Reason);
        if (booking.CurrentCancellation is { IsTerminal: false })
            return FlightsErrors.ConfirmationOutcomeUnknown;
        var now = time.GetUtcNow();
        var attemptId = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var fingerprint = Convert
            .ToHexString(
                SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            Version = 1,
                            cmd.AggregateId,
                            cmd.UserId,
                            booking.ProviderOrderId,
                            Amount = booking.TotalAmount!.Amount,
                            Currency = booking.TotalAmount.Currency.Value,
                            booking.HeldQuoteRevision,
                            booking.PassengerCount,
                        }
                    )
                )
            )
            .ToLowerInvariant();
        var started = booking.DecideConfirmationStart(
            cmd.UserId,
            attemptId,
            admission,
            stream.CurrentVersion ?? 0,
            fingerprint,
            now
        );
        if (started.Kind == CancellationDecisionKind.Rejected)
            return CancellationDecisionWriter.Reason(started.Reason);
        try
        {
            await CancellationDecisionWriter.Persist(
                started,
                stream,
                marten,
                outbox,
                [
                    new(
                        new ConfirmationBarrierDeadline(cmd.AggregateId, attemptId, admission),
                        now.AddSeconds(310)
                    ),
                ],
                ct
            );
        }
        catch (BookingWriteConflictException)
        {
            return FlightsErrors.ConcurrencyConflict;
        }
        var provider = bookingProviders.Single();
        var accepted = booking.TotalAmount!;
        var order = booking.ProviderOrderId!;
        var preflight = await RunStep(() =>
            provider.ValidateConfirmationAsync(order, accepted, ct)
        );
        await using (var claimSession = marten.DocumentStore.LightweightSession())
        {
            var current = await claimSession.Events.FetchForWriting<BookingAggregate>(
                cmd.AggregateId,
                ct
            );
            CancellationDecisionWriter.RequireSource(current.Aggregate);
            var aggregate = current.Aggregate!;
            if (aggregate.CurrentConfirmationAttempt is not { } attempt || attempt.Id != attemptId)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            if (preflight.IsError)
            {
                await CancellationDecisionWriter.Persist(
                    aggregate.DecideConfirmationNotDispatched(
                        attemptId,
                        attempt.Revision,
                        CancellationReason.ProviderUnavailable,
                        time.GetUtcNow()
                    ),
                    current,
                    claimSession,
                    outbox,
                    [],
                    ct
                );
                return SafePreflight(preflight.FirstError.Code);
            }
            var claimed = aggregate.DecideConfirmationEffectsClaim(
                attemptId,
                attempt.Revision,
                admission,
                instance.Id,
                time.GetUtcNow()
            );
            await CancellationDecisionWriter.Persist(
                claimed,
                current,
                claimSession,
                outbox,
                [],
                ct
            );
            if (!claimed.Events.OfType<ConfirmationEffectsClaimed>().Any())
                return FlightsErrors.ConfirmationOutcomeUnknown;
        }
        if (
            !await Continue(
                marten.DocumentStore,
                outbox,
                cmd.AggregateId,
                attemptId,
                admission,
                instance.Id,
                time,
                ct
            )
        )
            return FlightsErrors.ConfirmationOutcomeUnknown;
        var authorization = await RunStep(() =>
            payments.AuthorizeAsync(accepted, attemptId.ToString("N"), ct)
        );
        if (authorization.IsError)
        {
            await Manual(marten.DocumentStore, outbox, cmd.AggregateId, attemptId, time, ct);
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
        var payment = authorization.Value;
        // Even a late observed ref/capture fact may be retained; continuation authority is rechecked separately.
        await using (var referenceSession = marten.DocumentStore.LightweightSession())
        {
            var current = await referenceSession.Events.FetchForWriting<BookingAggregate>(
                cmd.AggregateId,
                ct
            );
            CancellationDecisionWriter.RequireSource(current.Aggregate);
            var attempt = current.Aggregate!.CurrentConfirmationAttempt;
            if (attempt?.Id != attemptId)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            var saved = current.Aggregate.DecideConfirmationPaymentReference(
                attemptId,
                attempt.Revision,
                instance.Id,
                payment,
                time.GetUtcNow()
            );
            if (saved.Kind == CancellationDecisionKind.Rejected)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            await CancellationDecisionWriter.Persist(
                saved,
                current,
                referenceSession,
                outbox,
                [],
                ct
            );
        }
        if (
            !await Continue(
                marten.DocumentStore,
                outbox,
                cmd.AggregateId,
                attemptId,
                admission,
                instance.Id,
                time,
                ct
            )
        )
            return FlightsErrors.ConfirmationOutcomeUnknown;
        var captured = await RunStep(() => payments.CaptureAsync(payment, ct));
        if (captured.IsError)
        {
            await Manual(marten.DocumentStore, outbox, cmd.AggregateId, attemptId, time, ct);
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
        await using (var captureSession = marten.DocumentStore.LightweightSession())
        {
            var current = await captureSession.Events.FetchForWriting<BookingAggregate>(
                cmd.AggregateId,
                ct
            );
            CancellationDecisionWriter.RequireSource(current.Aggregate);
            var attempt = current.Aggregate!.CurrentConfirmationAttempt;
            if (attempt?.Id != attemptId)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            var saved = current.Aggregate.DecideConfirmationCapture(
                attemptId,
                attempt.Revision,
                instance.Id,
                payment,
                accepted,
                time.GetUtcNow()
            );
            if (saved.Kind == CancellationDecisionKind.Rejected)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            await CancellationDecisionWriter.Persist(
                saved,
                current,
                captureSession,
                outbox,
                [],
                ct
            );
        }
        if (
            !await Continue(
                marten.DocumentStore,
                outbox,
                cmd.AggregateId,
                attemptId,
                admission,
                instance.Id,
                time,
                ct
            )
        )
            return FlightsErrors.ConfirmationOutcomeUnknown;
        var confirmed = await RunStep(() =>
            provider.ConfirmOrderAsync(
                order,
                payment,
                accepted,
                token =>
                    Continue(
                        marten.DocumentStore,
                        outbox,
                        cmd.AggregateId,
                        attemptId,
                        admission,
                        instance.Id,
                        time,
                        token
                    ),
                ct
            )
        );
        if (confirmed.IsError || confirmed.Value.PaymentEvidence is null)
        {
            await Manual(marten.DocumentStore, outbox, cmd.AggregateId, attemptId, time, ct);
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
        var confirmedStatus = "Confirmed";
        await using (var completionSession = marten.DocumentStore.LightweightSession())
        {
            var current = await completionSession.Events.FetchForWriting<BookingAggregate>(
                cmd.AggregateId,
                ct
            );
            CancellationDecisionWriter.RequireSource(current.Aggregate);
            var attempt = current.Aggregate!.CurrentConfirmationAttempt;
            if (attempt?.Id != attemptId)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            var completedDecision = current.Aggregate.DecideConfirmationComplete(
                attemptId,
                attempt.Revision,
                payment,
                confirmed.Value.ProviderOrderId,
                confirmed.Value.PaymentEvidence,
                CancellationResolutionSource.SupplierApi,
                time.GetUtcNow()
            );
            if (completedDecision.Kind == CancellationDecisionKind.Rejected)
            {
                await Manual(marten.DocumentStore, outbox, cmd.AggregateId, attemptId, time, ct);
                return FlightsErrors.ConfirmationOutcomeUnknown;
            }
            await CancellationDecisionWriter.Persist(
                completedDecision,
                current,
                completionSession,
                outbox,
                [],
                ct
            );
            confirmedStatus = current.Aggregate.Status.ToString();
        }
        metrics.RecordPaymentOutcome(true);
        metrics.RecordOrderBooked();
        return new ConfirmedOrderResult(
            cmd.AggregateId,
            confirmedStatus,
            payment.Value.ToString("N")
        );
    }

    private static async Task<bool> Continue(
        IDocumentStore store,
        IMartenOutbox outbox,
        Guid aggregateId,
        Guid attemptId,
        Guid admission,
        Guid sender,
        TimeProvider time,
        CancellationToken ct
    )
    {
        await using var session = store.LightweightSession();
        var stream = await session.Events.FetchForWriting<BookingAggregate>(aggregateId, ct);
        CancellationDecisionWriter.RequireSource(stream.Aggregate);
        var booking = stream.Aggregate!;
        var deadline = booking.DecideConfirmationDeadline(attemptId, admission, time.GetUtcNow());
        await CancellationDecisionWriter.Persist(deadline, stream, session, outbox, [], ct);
        return booking.CanContinueConfirmation(attemptId, sender);
    }

    private static async Task Manual(
        IDocumentStore store,
        IMartenOutbox outbox,
        Guid aggregateId,
        Guid attemptId,
        TimeProvider time,
        CancellationToken ct
    )
    {
        await using var session = store.LightweightSession();
        var stream = await session.Events.FetchForWriting<BookingAggregate>(aggregateId, ct);
        CancellationDecisionWriter.RequireSource(stream.Aggregate);
        var booking = stream.Aggregate!;
        if (booking.CurrentConfirmationAttempt is not { } attempt || attempt.Id != attemptId)
            return;
        await CancellationDecisionWriter.Persist(
            booking.DecideConfirmationManual(
                attemptId,
                attempt.Revision,
                CancellationReason.ManualVerificationRequired,
                time.GetUtcNow()
            ),
            stream,
            session,
            outbox,
            [],
            ct
        );
    }

    private static Error SafePreflight(string code) =>
        code == FlightsErrors.HoldExpired.Code ? FlightsErrors.HoldExpired
        : code == FlightsErrors.OrderPriceChanged.Code ? FlightsErrors.OrderPriceChanged
        : FlightsErrors.ConfirmationOutcomeUnknown;

    private static async Task<ErrorOr<T>> RunStep<T>(Func<Task<ErrorOr<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (TaskCanceledException e)
        {
            throw new TaskCanceledException(
                "Confirmation was cancelled.",
                null,
                e.CancellationToken
            );
        }
        catch (OperationCanceledException e)
        {
            throw new OperationCanceledException(
                "Confirmation was cancelled.",
                e.CancellationToken
            );
        }
        catch (Exception)
        {
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
    }
}
