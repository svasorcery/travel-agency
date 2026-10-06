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

public static class ExecuteCancellationPreparationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        ExecuteCancellationPreparation message,
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
        var booking = stream.Aggregate;
        CancellationDecisionWriter.RequireSource(booking);
        if (
            !booking!.CancellationOperations.TryGetValue(message.OperationId, out var op)
            || op.PreparationAdmissionId != message.AdmissionId
            || op.PreparationDispatchedAt is not null
        )
            return;
        var now = time.GetUtcNow();
        var decision = booking.DecideCancellationPreparationDispatch(
            op.Id,
            op.Revision,
            message.AdmissionId,
            instance.Id,
            Guid.NewGuid(),
            now
        );
        var claim = decision.Events.OfType<CancellationPreparationDispatched>().SingleOrDefault();
        await CancellationDecisionWriter.Persist(
            decision,
            stream,
            session,
            outbox,
            claim is null
                ? []
                : CancellationWorkFactory.Recovery(booking.Id, op.Id, claim.Recovery),
            ct
        );
        if (claim is null)
            return;
        ErrorOr<CancellationQuoteResult> result;
        try
        {
            result = await provider.CreateTermsAsync(op.ProviderOrderRef, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            result = new CancellationQuoteResult(
                CancellationQuoteOutcome.Unknown,
                null,
                CancellationReason.ProviderUnavailable
            );
        }
        // The supplier awaited call is never authority to append to the earlier stream snapshot.
        await using var fresh = store.LightweightSession();
        var current = await fresh.Events.FetchForWriting<BookingAggregate>(message.AggregateId, ct);
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
                CancellationUnknownStage.Preparation,
                CancellationReason.ProviderUnavailable,
                now
            );
        else if (result.Value.Outcome == CancellationQuoteOutcome.DefinitivelyRejected)
            completion = aggregate.DecideCancellationRefusal(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Preparation,
                true,
                result.Value.Reason,
                now
            );
        else if (
            result.Value.Quote is { } quote
            && result.Value.Outcome
                is CancellationQuoteOutcome.TermsAvailable
                    or CancellationQuoteOutcome.UnsupportedTerms
        )
        {
            var bound = CancellationScope.Create(aggregate);
            if (
                bound.IsError
                || bound.Value != op.ItineraryPartyHash
                || quote.ProviderOrderRef != op.ProviderOrderRef
                || (
                    quote.ItineraryPartyHash is not null
                    && quote.ItineraryPartyHash != op.ItineraryPartyHash
                )
            )
                completion = aggregate.DecideCancellationUnknown(
                    op.Id,
                    op.Revision,
                    CancellationUnknownStage.Preparation,
                    CancellationReason.InconsistentEvidence,
                    now
                );
            else
            {
                var terms = CancellationTerms.Create(
                    new(
                        1,
                        aggregate.Id,
                        op.OwnerId,
                        op.Id,
                        op.ProviderOrderRef,
                        quote.ProviderCancellationRef,
                        op.ItineraryPartyHash,
                        quote.Refund,
                        quote.Destination,
                        quote.Settlement,
                        quote.ExpiresAt,
                        "cancellation-v1"
                    ),
                    now
                );
                completion = terms.IsError
                    ? aggregate.DecideCancellationTermsUnavailable(
                        op.Id,
                        op.Revision,
                        true,
                        quote.ProviderCancellationRef,
                        CancellationReason.UnsupportedFinancialTerms,
                        now
                    )
                    : aggregate.DecideCancellationTerms(op.Id, op.Revision, terms.Value, now);
            }
        }
        else
            completion = aggregate.DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Preparation,
                result.Value.Reason == CancellationReason.None
                    ? CancellationReason.InvalidResponse
                    : result.Value.Reason,
                now
            );
        if (!result.IsError && result.Value.OrderCancellation is { } fact)
            completion = aggregate.DecideCancellationExternalFact(op.Id, op.Revision, fact, now);
        await CancellationDecisionWriter.Persist(completion, current, fresh, outbox, [], ct);
    }
}
