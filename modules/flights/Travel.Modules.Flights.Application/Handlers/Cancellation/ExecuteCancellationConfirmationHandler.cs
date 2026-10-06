using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class ExecuteCancellationConfirmationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        ExecuteCancellationConfirmation message,
        IDocumentSession session,
        IDocumentStore store,
        IMartenOutbox outbox,
        IFlightCancellationProvider provider,
        IDispatchInstanceIdentity instance,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            ct
        );
        CancellationDecisionWriter.RequireSource(stream.Aggregate);
        if (
            !stream.Aggregate!.CancellationOperations.TryGetValue(message.OperationId, out var op)
            || op.ConfirmationAdmissionId != message.AdmissionId
            || op.ConfirmationDispatchedAt is not null
            || op.Phase != CancellationPhase.Accepted
            || op.Terms is null
        )
            return;
        ErrorOr<CancellationEligibility> eligibility;
        try
        {
            eligibility = await provider.InspectOrderAsync(op.ProviderOrderRef, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            eligibility = Error.Failure(
                "Flights.CancellationUnavailable",
                "Supplier facts unavailable."
            );
        }

        await using var claimSession = store.LightweightSession();
        var candidate = await claimSession.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            ct
        );
        CancellationDecisionWriter.RequireSource(candidate.Aggregate);
        var booking = candidate.Aggregate!;
        if (
            !booking.CancellationOperations.TryGetValue(message.OperationId, out op)
            || op.ConfirmationAdmissionId != message.AdmissionId
            || op.ConfirmationDispatchedAt is not null
            || op.Phase != CancellationPhase.Accepted
            || op.Terms is null
        )
            return;
        var now = time.GetUtcNow();
        var scope = CancellationScope.Create(booking);
        var reason =
            eligibility.IsError ? CancellationReason.ProviderUnavailable
            : eligibility.Value.ProviderOrderRef != op.ProviderOrderRef
                ? CancellationReason.IdentityMismatch
            : !eligibility.Value.CancellationAvailable
            || eligibility.Value.CurrentCancellationRef is not null
                ? CancellationReason.NotCancellable
            : op.Terms.Settlement.UnpaidOrderVerified
            && eligibility.Value.PaymentState != CancellationProviderPaymentState.AwaitingPayment
                ? CancellationReason.InconsistentEvidence
            : scope.IsError || scope.Value != op.ItineraryPartyHash
                ? CancellationReason.StaleProposal
            : op.Terms.ExpiresAt <= now ? CancellationReason.TermsExpired
            : CancellationReason.None;
        if (reason != CancellationReason.None)
        {
            var blocked =
                !eligibility.IsError && eligibility.Value.OrderCancellation is { } preflightFact
                    ? booking.DecideCancellationExternalFact(op.Id, op.Revision, preflightFact, now)
                    : booking.DecideCancellationNotDispatched(
                        op.Id,
                        op.Revision,
                        CancellationUnknownStage.Confirmation,
                        reason,
                        now
                    );
            await CancellationDecisionWriter.Persist(
                blocked,
                candidate,
                claimSession,
                outbox,
                [],
                ct
            );
            return;
        }
        var decision = booking.DecideCancellationConfirmationDispatch(
            op.Id,
            op.Revision,
            message.AdmissionId,
            instance.Id,
            Guid.NewGuid(),
            now
        );
        var claim = decision.Events.OfType<CancellationConfirmationDispatched>().SingleOrDefault();
        await CancellationDecisionWriter.Persist(
            decision,
            candidate,
            claimSession,
            outbox,
            claim is null
                ? []
                : CancellationWorkFactory.Recovery(booking.Id, op.Id, claim.Recovery),
            ct
        );
        if (claim is null)
            return;
        var accepted = op.Terms;
        ErrorOr<CancellationEffectResult> result;
        try
        {
            result = await provider.ConfirmAsync(accepted, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            result = new CancellationEffectResult(
                CancellationEffectOutcome.Unknown,
                null,
                CancellationReason.ProviderUnavailable
            );
        }

        await using var finalSession = store.LightweightSession();
        var current = await finalSession.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            ct
        );
        CancellationDecisionWriter.RequireSource(current.Aggregate);
        var aggregate = current.Aggregate!;
        if (!aggregate.CancellationOperations.TryGetValue(message.OperationId, out op))
            return;
        now = time.GetUtcNow();
        CancellationDecision completion;
        if (result.IsError)
            completion = aggregate.DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                CancellationReason.ProviderUnavailable,
                now
            );
        else if (result.Value.Outcome == CancellationEffectOutcome.DefinitivelyRejected)
            completion = aggregate.DecideCancellationRefusal(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                true,
                result.Value.Reason,
                now
            );
        else if (
            result.Value.Outcome == CancellationEffectOutcome.Confirmed
            && result.Value.Observation is { } observation
            && observation.Quote is { } quote
            && quote.ProviderOrderRef == observation.ProviderOrderRef
            && quote.ProviderCancellationRef == observation.ProviderCancellationRef
        )
        {
            var proof = CancellationEvidence.FromSupplierConfirmation(
                accepted,
                new(
                    observation.ProviderOrderRef,
                    observation.ProviderCancellationRef ?? "",
                    quote.ItineraryPartyHash ?? "",
                    quote.Refund,
                    quote.Destination,
                    quote.Settlement,
                    observation.ConfirmedAt,
                    observation.ObservedAt,
                    observation.Source
                ),
                now
            );
            completion = proof.IsError
                ? aggregate.DecideCancellationUnknown(
                    op.Id,
                    op.Revision,
                    CancellationUnknownStage.Confirmation,
                    CancellationReason.InconsistentEvidence,
                    now
                )
                : aggregate.DecideCancellationSuccess(op.Id, op.Revision, proof.Value, now);
        }
        else
            completion = aggregate.DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                result.Value.Reason == CancellationReason.None
                    ? CancellationReason.InvalidResponse
                    : result.Value.Reason,
                now
            );
        if (
            !result.IsError
            && result.Value.Observation?.OrderCancellation is { } fact
            && !completion.Events.Any(e => e is CancellationSucceeded)
        )
            completion = aggregate.DecideCancellationExternalFact(op.Id, op.Revision, fact, now);
        await CancellationDecisionWriter.Persist(completion, current, finalSession, outbox, [], ct);
    }
}
