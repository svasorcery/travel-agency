using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class AncillaryConfirmationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Invalid_optional_service_facts_cannot_be_attached_as_a_known_order(
        bool invalidBaggageLimit
    )
    {
        var (booking, actual, now) = Held(false);
        var line = actual.Services[0];
        var invalid = actual with
        {
            Services = new([
                invalidBaggageLimit
                    ? line with
                    {
                        Baggage = new(MaximumWeightKg: -1),
                    }
                    : line with
                    {
                        Disclosures = new(["Invalid\tcontrol"]),
                    },
            ]),
        };
        var attempt = booking.CurrentCreation!;
        booking
            .DecideCreationObservation(
                attempt.Id,
                attempt.Revision,
                new(
                    BookingCreationOutcome.CreatedWithDifferences,
                    invalid,
                    actual.ProviderOrderId,
                    true,
                    true,
                    "OperatorOrderObserved",
                    now,
                    BookingEvidenceSource.OperatorVerified
                ),
                now
            )
            .IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actual_service_evidence_cannot_attach_foreign_passenger_or_segment(
        bool foreignPassenger
    )
    {
        var (booking, actual, now) = Held(false);
        var line = actual.Services[0];
        var invalid = actual with
        {
            Services = new([
                foreignPassenger
                    ? line with
                    {
                        PassengerId = Guid.NewGuid(),
                    }
                    : line with
                    {
                        Segments = new([new(3, 0)]),
                    },
            ]),
        };
        var attempt = booking.CurrentCreation!;
        booking
            .DecideCreationObservation(
                attempt.Id,
                attempt.Revision,
                new(
                    BookingCreationOutcome.CreatedWithDifferences,
                    invalid,
                    actual.ProviderOrderId,
                    true,
                    true,
                    "OperatorOrderObserved",
                    now,
                    BookingEvidenceSource.OperatorVerified
                ),
                now
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Privileged_attestation_can_attach_complete_matching_order_after_sender_is_stopped()
    {
        var (booking, actual, now) = Held(false);
        var attempt = booking.CurrentCreation!;
        var evidence = new ManualResolutionEvidence(
            "EVIDENCE-001",
            ManualEvidenceCategory.SupplierSupportAttestation,
            now,
            ProviderOrderRef: actual.ProviderOrderId,
            QuiescenceRef: "STOPPED-001",
            StoppedAt: now,
            StoppedInstanceIds: new([attempt.SenderInstanceId]),
            SenderEgressIsolated: true,
            OldInstancesCannotResume: true,
            CreationEvidence: new(
                attempt.Id,
                attempt.OwnerId,
                attempt.QuoteRevision,
                true,
                false,
                actual
            )
        );
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Creation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.AttachMatches,
            evidence
        );
        var result = booking.DecideManualResolution(Guid.NewGuid(), input, now);
        result.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        result
            .Events.OfType<BookingCreationObserved>()
            .Single()
            .Observation.Outcome.ShouldBe(BookingCreationOutcome.Matches);
        result.Events.OfType<OfferHeldV3>().Count().ShouldBe(1);
        booking
            .DecideManualResolution(
                Guid.NewGuid(),
                input with
                {
                    ResolutionId = Guid.NewGuid(),
                    Evidence = evidence with
                    {
                        CreationEvidence = evidence.CreationEvidence! with
                        {
                            OwnerId = Guid.NewGuid(),
                        },
                    },
                },
                now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Negative_operator_closure_requires_affirmative_no_created_or_pending_order()
    {
        var (booking, _, now) = Held(false);
        var attempt = booking.CurrentCreation!;
        var evidence = new ManualResolutionEvidence(
            "EVIDENCE-002",
            ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation,
            now,
            QuiescenceRef: "STOPPED-002",
            StoppedAt: now,
            StoppedInstanceIds: new([attempt.SenderInstanceId]),
            SupplierFinalNoEffectsConfirmed: true,
            SenderEgressIsolated: true,
            OldInstancesCannotResume: true,
            CreationEvidence: new(attempt.Id, attempt.OwnerId, attempt.QuoteRevision, true, false)
        );
        var input = new ManualResolutionInput(
            booking.Id,
            ManualResolutionTargetKind.Creation,
            attempt.Id,
            attempt.Revision,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.ConfirmNoCreatedOrder,
            evidence
        );
        booking
            .DecideManualResolution(Guid.NewGuid(), input, now)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        booking
            .DecideManualResolution(
                Guid.NewGuid(),
                input with
                {
                    Evidence = evidence with
                    {
                        CreationEvidence = evidence.CreationEvidence! with
                        {
                            NoCreatedOrPendingOrderConfirmed = true,
                        },
                    },
                },
                now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Allowed);
    }

    [Fact]
    public void Before_wallet_difference_closes_unclaimed_attempt_and_preserves_actual_services()
    {
        var (booking, actual, now) = Held();
        var confirmation = Guid.NewGuid();
        booking.Apply(
            new ConfirmationAttemptStarted(
                confirmation,
                booking.OwnerUserId!.Value,
                Guid.NewGuid(),
                new string('a', 64),
                actual.ProviderOrderId,
                actual.Total,
                now
            )
        );
        var different = actual with
        {
            Services = new([]),
            Total = Money.Create(50, actual.Total.Currency).Value,
        };
        var decision = booking.DecideConfirmationServiceDifference(different, now);
        decision.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        var observed = decision.Events.OfType<BookingCreationObserved>().Single();
        observed.Observation.Outcome.ShouldBe(BookingCreationOutcome.CreatedWithDifferences);
        decision.Events.OfType<ConfirmationAttemptClosedWithoutEffects>().Count().ShouldBe(1);
        booking.Apply(observed);
        booking.Apply(decision.Events.OfType<ConfirmationAttemptClosedWithoutEffects>().Single());
        booking.TotalAmount!.Amount.ShouldBe(50);
        booking.CurrentCreation!.Accepted.Total.Amount.ShouldBe(70);
        booking.CurrentCreation.CreationCompleted.ShouldBeTrue();
        booking.CreationBlocksConfirmation.ShouldBeTrue();
        booking.HasConfirmationBarrier.ShouldBeFalse();
    }

    [Fact]
    public void Money_receipt_cannot_complete_service_purchase_without_matching_typed_proof()
    {
        var (booking, actual, now) = Held();
        var id = Guid.NewGuid();
        var payment = new PaymentRef(Guid.NewGuid());
        booking.Apply(
            new ConfirmationAttemptStarted(
                id,
                booking.OwnerUserId!.Value,
                Guid.NewGuid(),
                new string('a', 64),
                actual.ProviderOrderId,
                actual.Total,
                now
            )
        );
        booking.Apply(
            new ConfirmationEffectsClaimed(
                id,
                booking.CurrentConfirmationAttempt!.AdmissionId,
                Guid.NewGuid(),
                now
            )
        );
        booking.Apply(new ConfirmationPaymentReferenceRecorded(id, payment, now));
        booking.Apply(new ConfirmationCaptureObserved(id, payment, actual.Total, now));
        var receipt = SupplierPaymentEvidence
            .Create("pay_fictional", actual.Total, SupplierPaymentKind.Balance)
            .Value;
        var revision = booking.CurrentConfirmationAttempt!.Revision;
        booking
            .DecideConfirmationComplete(
                id,
                revision,
                payment,
                actual.ProviderOrderId,
                receipt,
                CancellationResolutionSource.SupplierApi,
                now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        var proof = new BookingServiceProof(
            booking.CurrentCreation!.QuoteRevision,
            actual.ProviderOrderId,
            actual.Services
        );
        booking
            .DecideConfirmationComplete(
                id,
                revision,
                payment,
                actual.ProviderOrderId,
                receipt,
                CancellationResolutionSource.SupplierApi,
                now,
                proof with
                {
                    Services = new([]),
                }
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        booking
            .DecideConfirmationComplete(
                id,
                revision,
                payment,
                actual.ProviderOrderId,
                receipt,
                CancellationResolutionSource.SupplierApi,
                now,
                proof
            )
            .Kind.ShouldBe(CancellationDecisionKind.Allowed);
    }

    internal static (BookingAggregate Booking, BookedOrderFacts Actual, DateTimeOffset Now) Held(
        bool complete = true
    )
    {
        var clock = AncillaryCatalogTests.Clock;
        var dto = AncillaryCatalogTests.Offer();
        var offer = DuffelOfferMapper.Map(dto, clock).Value;
        var binding = QuoteBindingFactory.Create(offer.Party, 2, null).Value;
        var catalog = DuffelAncillaryMapper.Map(dto, null, false, clock).Value;
        var owner = Guid.NewGuid();
        var purchase = BookingPurchaseFactory
            .Select(catalog, binding, owner, [new(catalog.Services[0].Key, 2)])
            .Value;
        var booking = new BookingAggregate();
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Id))!
            .SetValue(booking, Guid.NewGuid());
        var now = clock.GetUtcNow();
        booking.Apply(
            new OfferQuoted(
                offer.Id,
                offer.Itinerary,
                offer.TotalAmount,
                offer.ExpiresAt,
                offer.ProviderOfferRef,
                now,
                offer.FareConditions,
                binding
            )
        );
        booking.Apply(new BookingPurchaseQuoted(purchase, now));
        var protectedParty = ProtectedPassengerPartySnapshot
            .Create(1, "opaque-fictional-envelope")
            .Value;
        var start = booking
            .DecideCreationStart(
                owner,
                Guid.NewGuid(),
                new string('a', 64),
                binding.Revision,
                protectedParty,
                Guid.NewGuid(),
                now,
                true
            )
            .Value.Event!;
        booking.Apply(start);
        var actual = DuffelBookedServicesMapper
            .Map(
                BookedServiceMappingTests.Order(dto, start.AttemptId),
                offer,
                binding,
                purchase,
                start.AttemptId,
                clock
            )
            .Value;
        if (complete)
        {
            booking.Apply(
                booking
                    .DecideCreationObservation(
                        start.AttemptId,
                        1,
                        new(
                            BookingCreationOutcome.Matches,
                            actual,
                            actual.ProviderOrderId,
                            true,
                            true,
                            "OrderObserved",
                            now
                        ),
                        now
                    )
                    .Value.Event!
            );
            booking.Apply(
                new OfferHeldV3(
                    actual.ProviderOrderId,
                    protectedParty,
                    actual.PaymentRequiredBy,
                    now,
                    owner,
                    binding.Revision,
                    2
                )
            );
            booking.Apply(new BookingMutationCoordinationEnabled(now));
        }
        return (booking, actual, now);
    }
}
