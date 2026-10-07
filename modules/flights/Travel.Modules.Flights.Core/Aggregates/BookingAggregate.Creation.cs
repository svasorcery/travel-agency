using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    public CancellationDecision DecideConfirmationServiceDifference(
        BookedOrderFacts actual,
        DateTimeOffset now
    )
    {
        if (
            CurrentCreation is not { CreationCompleted: true } creation
            || CurrentConfirmationAttempt
                is not { IsTerminal: false, EffectsClaimedAt: null } financial
            || actual is null
            || !actual.AwaitingPayment
            || actual.Matches(creation.Accepted)
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        var observed = DecideCreationObservation(
            creation.Id,
            creation.Revision,
            new(
                BookingCreationOutcome.CreatedWithDifferences,
                actual,
                actual.ProviderOrderId,
                true,
                true,
                "ServicesChanged",
                now
            ),
            now
        );
        if (observed.IsError || observed.Value.Event is null)
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AllowCancellation(
            financial.Id,
            observed.Value.Event,
            new ConfirmationAttemptClosedWithoutEffects(
                financial.Id,
                CancellationReason.InconsistentEvidence,
                now
            )
        );
    }

    private readonly Dictionary<Guid, BookingCreationAttempt> creationAttempts = new();
    public BookingPurchase? Purchase { get; private set; }
    public Guid? CurrentCreationId { get; private set; }
    public BookingCreationAttempt? CurrentCreation =>
        CurrentCreationId is { } id && creationAttempts.TryGetValue(id, out var attempt)
            ? attempt
            : null;
    public IReadOnlyDictionary<Guid, BookingCreationAttempt> CreationAttempts =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, BookingCreationAttempt>(
            creationAttempts
        );
    public bool HasCreationBarrier =>
        CurrentCreation is { Outcome: not BookingCreationOutcome.NotCreated };
    public bool HasUnresolvedCreation => CurrentCreation is { IsUnresolved: true };
    public bool CreationBlocksConfirmation =>
        CurrentCreation is { Outcome: not BookingCreationOutcome.Matches };

    public void Apply(BookingCreationObserved e)
    {
        var prior = creationAttempts[e.AttemptId];
        var observation = e.Observation;
        var created =
            observation.Order is not null
            && observation.Outcome
                is BookingCreationOutcome.Matches
                    or BookingCreationOutcome.CreatedWithDifferences;
        var updated = prior with
        {
            Revision = prior.Revision + 1,
            Outcome = observation.Outcome,
            Actual = observation.Order ?? prior.Actual,
            KnownOrderId = observation.ReceiptCorrelated
                ? observation.KnownOrderId ?? prior.KnownOrderId
                : prior.KnownOrderId,
            SenderCompleted = prior.SenderCompleted || observation.SenderCompleted,
            CreationCompleted = prior.CreationCompleted || created,
            HadUnknown =
                prior.HadUnknown
                || observation.Outcome == BookingCreationOutcome.ManualReviewRequired,
            Reason = observation.Reason,
            Source = observation.Source,
            ObservedAt = observation.ObservedAt,
        };
        creationAttempts[e.AttemptId] = updated;
        if (e.AttemptId == CurrentCreationId && created)
        {
            TotalAmount = observation.Order!.Total;
            Itinerary = observation.Order.Itinerary;
        }
    }

    public ErrorOr<CreationObservationDecision> DecideCreationObservation(
        Guid attemptId,
        long expectedRevision,
        BookingCreationObservation observation,
        DateTimeOffset now
    )
    {
        var invalid = Error.Conflict(
            "Flights.CreationEvidenceInvalid",
            "Creation evidence is inconsistent."
        );
        if (
            !HasConsistentMutationOwner
            || !creationAttempts.TryGetValue(attemptId, out var prior)
            || attemptId != CurrentCreationId
            || prior.Revision != expectedRevision
        )
            return Error.Conflict("Flights.ConcurrencyConflict", "Creation revision changed.");
        if (
            observation is null
            || !Enum.IsDefined(observation.Outcome)
            || !Enum.IsDefined(observation.Source)
            || observation.Source == BookingEvidenceSource.None
            || observation.ObservedAt == default
            || observation.ObservedAt > now
            || observation.Reason is null
            || observation.Reason.Length > 80
            || observation.Reason.Any(char.IsControl)
        )
            return invalid;
        if (observation.Outcome == BookingCreationOutcome.NotCreated)
        {
            if (
                prior.CreationCompleted
                || !observation.PositiveNoEffects
                || !observation.SenderCompleted
                || prior.HadUnknown && observation.Source != BookingEvidenceSource.OperatorVerified
            )
                return invalid;
        }
        else if (
            observation.Outcome
            is BookingCreationOutcome.Matches
                or BookingCreationOutcome.CreatedWithDifferences
        )
        {
            if (
                prior.Outcome == BookingCreationOutcome.NotCreated
                || prior.Outcome == BookingCreationOutcome.CreatedWithDifferences
                    && observation.Outcome == BookingCreationOutcome.Matches
            )
                return invalid;
            var actual = observation.Order;
            if (
                !observation.ReceiptCorrelated
                || !observation.SenderCompleted
                || actual is null
                || string.IsNullOrWhiteSpace(actual.ProviderOrderId)
                || actual.ProviderOrderId != observation.KnownOrderId
                || actual.ProviderOfferRef != ProviderOfferRef
                || prior.KnownOrderId is { } known && known != actual.ProviderOrderId
                || actual.Cancelled
                || !prior.CreationCompleted
                    && (!actual.AwaitingPayment || actual.PaymentRequiredBy == default)
                || actual.Total is null
                || actual.Total.Amount < 0
                || actual.Total.Currency is null
                || !BookingServiceProof.ValidLines(actual.Services)
                || actual.Itinerary is null
                || actual.Services.Any(s =>
                    !actual.PassengerIds.Contains(s.PassengerId)
                    || s.LineTotal.Currency != actual.Total.Currency
                    || s.Segments.Any(address =>
                        address.Leg >= actual.Itinerary.Slices.Count
                        || address.Segment >= actual.Itinerary.Slices[address.Leg].Segments.Count
                    )
                )
                || actual
                    .Services.Where(s => s.Kind == BookingServiceKind.Seat)
                    .GroupBy(s => (s.PassengerId, s.Segments[0]))
                    .Any(g => g.Count() > 1)
                || actual
                    .Services.Where(s => s.Kind == BookingServiceKind.Seat)
                    .GroupBy(s => (s.Segments[0], s.SeatDesignator!.ToUpperInvariant()))
                    .Any(g => g.Count() > 1)
                || QuoteBinding is null
                || actual.PassengerIds.Count != QuoteBinding.Slots.Count
                || actual.PassengerIds.Distinct().Count() != actual.PassengerIds.Count
                || !actual
                    .PassengerIds.ToHashSet()
                    .SetEquals(QuoteBinding.Slots.Select(s => s.Id.Value))
                || Itinerary is null
                || !BookedOrderFacts.SameItinerary(Itinerary, actual.Itinerary)
            )
                return invalid;
            var matches = actual.Matches(prior.Accepted);
            if (matches != (observation.Outcome == BookingCreationOutcome.Matches))
                return invalid;
        }
        else if (prior.CreationCompleted || prior.Outcome == BookingCreationOutcome.NotCreated)
            return new CreationObservationDecision(false, null);
        if (
            prior.Outcome == observation.Outcome
            && prior.KnownOrderId == observation.KnownOrderId
            && prior.Reason == observation.Reason
            && prior.SenderCompleted == observation.SenderCompleted
            && JsonSerializer.Serialize(prior.Actual) == JsonSerializer.Serialize(observation.Order)
        )
            return new CreationObservationDecision(false, null);
        return new CreationObservationDecision(
            true,
            new BookingCreationObserved(attemptId, observation, now)
        );
    }

    public void Apply(BookingPurchaseQuoted e)
    {
        if (
            CurrentCreation is { Outcome: BookingCreationOutcome.NotCreated } prior
            && prior.QuoteRevision != e.Purchase.QuoteRevision
        )
            CurrentCreationId = null;
        Purchase = e.Purchase;
        TotalAmount = e.Purchase.Total;
        if (e.Purchase.OwnerId is { } owner)
            OwnerUserId = owner;
    }

    public void Apply(BookingCreationStarted e)
    {
        creationAttempts.Add(
            e.AttemptId,
            new(
                e.AttemptId,
                e.OwnerId,
                e.Digest,
                e.QuoteRevision,
                e.Accepted,
                e.ProtectedParty,
                e.SenderInstanceId,
                e.OccurredAt
            )
        );
        CurrentCreationId = e.AttemptId;
        OwnerUserId = e.OwnerId;
    }

    public ErrorOr<CreationAdmission> DecideCreationStart(
        Guid owner,
        Guid requestId,
        string digest,
        Guid quoteRevision,
        ProtectedPassengerPartySnapshot party,
        Guid sender,
        DateTimeOffset now,
        bool acceptAncillaries
    )
    {
        if (owner == Guid.Empty || OwnerUserId is { } savedOwner && savedOwner != owner)
            return Error.NotFound("Flights.OfferNotFound", "Booking is unavailable.");
        if (
            requestId == Guid.Empty
            || sender == Guid.Empty
            || string.IsNullOrEmpty(digest)
            || digest.Length != 64
            || digest.Any(c => !Uri.IsHexDigit(c))
        )
            return Error.Validation(
                "Flights.CreationRequestInvalid",
                "Creation identity is invalid."
            );
        digest = digest.ToLowerInvariant();
        if (creationAttempts.TryGetValue(requestId, out var prior))
            return
                prior.OwnerId == owner
                && prior.Digest == digest
                && prior.QuoteRevision == quoteRevision
                ? new CreationAdmission(false, prior.Id, null)
                : Error.Conflict(
                    "Flights.IdempotencyConflict",
                    "Request identity has different content."
                );
        if (HasCreationBarrier)
            return Error.Conflict(
                "Flights.HoldOutcomeUnknown",
                "A retained creation already exists."
            );
        if (CurrentCreation is { Outcome: BookingCreationOutcome.NotCreated })
            return Error.Conflict(
                "Flights.QuoteRevisionMismatch",
                "Refresh the quote before a new creation attempt."
            );
        if (HasConfirmationBarrier || CurrentCancellation is { IsTerminal: false })
            return Error.Conflict(
                "Flights.HoldOutcomeUnknown",
                "Another booking mutation is unresolved."
            );
        var hold = DecideHold(now, quoteRevision, PassengerCount);
        if (hold is BookingTransitionDecision.Rejected rejected)
            return Error.Conflict(
                "Flights." + rejected.Reason.Code,
                "Current quote cannot be held."
            );
        if (
            party is null
            || party.FormatVersion != 1
            || string.IsNullOrWhiteSpace(party.Ciphertext)
        )
            return Error.Validation("Flights.PiiEnvelopeInvalid", "Protected party is invalid.");
        if (
            Itinerary is null
            || TotalAmount?.Currency is null
            || now == default
            || now > DateTimeOffset.MaxValue.AddSeconds(-180)
        )
            return Error.Validation(
                "Flights.CreationRequestInvalid",
                "Creation metadata is invalid."
            );
        var accepted =
            Purchase ?? BookingPurchase.Empty(quoteRevision, TotalAmount!, ExpiresAt!.Value);
        if (
            accepted.QuoteRevision != quoteRevision
            || accepted.ExpiresAt <= now
            || accepted.OwnerId is { } purchaseOwner && purchaseOwner != owner
        )
            return Error.Conflict(
                "Flights.QuoteRevisionMismatch",
                "Purchase no longer matches the quote."
            );
        if (accepted.HasServices && !acceptAncillaries)
            return Error.Validation(
                "Flights.AncillaryConsentRequired",
                "Explicit service acceptance is required."
            );
        return new CreationAdmission(
            true,
            requestId,
            new BookingCreationStarted(
                requestId,
                owner,
                digest,
                quoteRevision,
                accepted,
                party,
                sender,
                now
            )
        );
    }
}
