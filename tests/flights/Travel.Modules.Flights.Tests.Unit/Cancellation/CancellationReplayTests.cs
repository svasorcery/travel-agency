using System.Reflection;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Tests.Unit.Aggregates;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationReplayTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;
    private static readonly Guid Sender = Guid.NewGuid();
    private static readonly Guid Admission = Guid.NewGuid();
    private static readonly Guid Epoch = Guid.NewGuid();
    private static readonly Guid Attempt = Guid.NewGuid();
    private static readonly Guid Read = Guid.NewGuid();
    private static readonly PaymentRef Payment = PaymentRef.New();

    public static IEnumerable<object[]> MetadataEvents()
    {
        var input = CancellationTestData.Input();
        var op = input.OperationId;
        var terms = CancellationTerms.Create(input, Now).Value;
        var schedule = RecoverySchedule.Create(Epoch, Now).Value;
        var money = terms.Refund;
        var observation = new CancellationObservation(
            input.ProviderOrderRef,
            input.ProviderCancellationRef,
            CancellationObservationState.Pending,
            null,
            null,
            Now,
            CancellationResolutionSource.SupplierApi
        );
        var receipt = SupplierPaymentEvidence
            .Create("pay_fictional", money, SupplierPaymentKind.Balance)
            .Value;
        var evidence = CancellationEvidence
            .FromSupplierConfirmation(
                terms,
                new(
                    input.ProviderOrderRef,
                    input.ProviderCancellationRef,
                    input.ItineraryPartyHash,
                    money,
                    terms.Destination,
                    terms.Settlement,
                    Now,
                    Now,
                    CancellationResolutionSource.SupplierApi
                ),
                Now
            )
            .Value;
        IDomainEvent[] events =
        [
            new BookingMutationCoordinationEnabled(Now),
            new CancellationPreparationStarted(
                op,
                input.OwnerId,
                input.ProviderOrderRef,
                input.ItineraryPartyHash,
                new string('a', 64),
                Admission,
                Now
            ),
            new CancellationPreparationDispatched(op, Admission, Sender, schedule, Now),
            new CancellationTermsObtained(op, terms, Now),
            new CancellationTermsWereUnavailable(
                op,
                true,
                input.ProviderCancellationRef,
                CancellationReason.UnsupportedFinancialTerms,
                Now
            ),
            new CancellationTermsAccepted(
                op,
                terms.Revision,
                terms.Hash,
                terms.NoticeVersion,
                new string('b', 64),
                Admission,
                Now
            ),
            new CancellationConfirmationDispatched(op, Admission, Sender, schedule, Now),
            new CancellationObservationStarted(
                op,
                Epoch,
                0,
                Read,
                schedule.Reserve(0, Now.AddSeconds(2)).UpdatedSchedule,
                Now
            ),
            new CancellationObservationRecorded(op, Read, observation, Now),
            new CancellationOutcomeBecameUnknown(
                op,
                CancellationUnknownStage.Confirmation,
                CancellationReason.Pending,
                Now
            ),
            new CancellationSucceeded(op, evidence, Now),
            new CancellationRejected(
                op,
                CancellationUnknownStage.Confirmation,
                CancellationResolutionSource.SupplierApi,
                CancellationReason.NotCancellable,
                Now
            ),
            new CancellationManualReviewRequired(
                op,
                CancellationUnknownStage.Confirmation,
                CancellationReason.Uncorrelated,
                false,
                Now
            ),
            new BookingOperationReviewRecorded(
                ManualResolutionTargetKind.Cancellation,
                op,
                Guid.NewGuid(),
                Guid.NewGuid(),
                new string('c', 64),
                ManualResolutionDecisionKind.RecordInconclusive,
                new("SUPPORT-0001", ManualEvidenceCategory.Inconclusive, Now),
                CancellationResolutionSource.OperatorVerified,
                Now
            ),
            new CancellationReviewWasAbandoned(op, Now),
            new CancellationReviewExpired(op, Now),
            new CancellationRefreshRequested(
                op,
                Guid.NewGuid(),
                new string('d', 64),
                Now.AddSeconds(60),
                Now
            ),
            new ConfirmationAttemptStarted(
                Attempt,
                input.OwnerId,
                Admission,
                new string('e', 64),
                input.ProviderOrderRef,
                money,
                Now
            ),
            new ConfirmationEffectsClaimed(Attempt, Admission, Sender, Now),
            new ConfirmationPaymentReferenceRecorded(Attempt, Payment, Now),
            new ConfirmationCaptureObserved(Attempt, Payment, money, Now),
            new ConfirmationAttemptCompleted(
                Attempt,
                Payment,
                money,
                input.ProviderOrderRef,
                receipt,
                CancellationResolutionSource.SupplierApi,
                Now
            ),
            new ConfirmationAttemptClosedWithoutEffects(Attempt, CancellationReason.None, Now),
            new ConfirmationAttemptRequiredManualReview(
                Attempt,
                CancellationReason.ManualVerificationRequired,
                Now
            ),
        ];
        return events.Select(e => new object[] { e });
    }

    [Theory]
    [MemberData(nameof(MetadataEvents))]
    public void Every_new_event_roundtrips_and_applies_without_clock_keys_or_fresh_validation(
        IDomainEvent source
    )
    {
        var json = JsonSerializer.Serialize(source, source.GetType());
        var restored = JsonSerializer.Deserialize(json, source.GetType())!;
        JsonSerializer.Serialize(restored, source.GetType()).ShouldBe(json);
        var booking = CancellationDecisionTests.Held();
        var data = CancellationTestData.Input();
        if (source is not CancellationPreparationStarted)
            booking.Apply(
                new CancellationPreparationStarted(
                    data.OperationId,
                    data.OwnerId,
                    data.ProviderOrderRef,
                    data.ItineraryPartyHash,
                    new string('a', 64),
                    Admission,
                    Now
                )
            );
        if (source is not ConfirmationAttemptStarted)
            booking.Apply(
                new ConfirmationAttemptStarted(
                    Attempt,
                    data.OwnerId,
                    Admission,
                    new string('e', 64),
                    data.ProviderOrderRef,
                    data.Refund!,
                    Now
                )
            );
        typeof(BookingAggregate)
            .GetMethod("Apply", new[] { source.GetType() })!
            .Invoke(booking, new[] { restored });
        booking.Status.ShouldBe(BookingStatus.Held);
        booking.OwnerUserId.ShouldBe(data.OwnerId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Frozen_held_versions_preserve_data_and_have_no_implicit_mutation_marker(int version)
    {
        var booking = new BookingAggregate();
        booking.Apply(
            JsonSerializer.Deserialize<OfferQuoted>(MultiLegReplayTests.HistoricalQuoteJson)!
        );
        switch (version)
        {
            case 1:
                booking.Apply(
                    JsonSerializer.Deserialize<OfferHeld>(MultiLegReplayTests.HistoricalHeldV1Json)!
                );
                break;
            case 2:
                booking.Apply(
                    JsonSerializer.Deserialize<OfferHeldV2>(
                        MultiLegReplayTests.HistoricalHeldV2Json
                    )!
                );
                break;
            case 3:
                booking.Apply(
                    JsonSerializer.Deserialize<OfferHeldV3>(
                        MultiLegReplayTests.HistoricalHeldV3Json
                    )!
                );
                break;
        }
        booking.MutationCoordinationEnabled.ShouldBeFalse();
        booking.CurrentCancellation.ShouldBeNull();
        booking.CurrentConfirmationAttempt.ShouldBeNull();
        booking.PassengerCount.ShouldBe(version == 3 ? 2 : 1);
        booking.Itinerary!.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(18));
        booking.BookedAt!.Value.Offset.ShouldBe(TimeSpan.FromHours(3));
    }

    [Fact]
    public void Late_matching_success_preserves_airline_refund_without_duplicate_cancellation_fact()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var terms = op.Terms!;
        booking.Apply(
            new OrderRefunded(
                RefundRef.New(),
                CancellationTestData.Money(),
                RefundInitiator.Airline,
                Now
            )
        );
        var refundTime = booking.RefundedAt;
        var evidence = CancellationEvidence
            .FromSupplierConfirmation(
                terms,
                new(
                    terms.ProviderOrderRef,
                    terms.ProviderCancellationRef,
                    terms.ItineraryPartyHash,
                    terms.Refund,
                    terms.Destination,
                    terms.Settlement,
                    Now,
                    Now,
                    CancellationResolutionSource.SupplierApi
                ),
                Now
            )
            .Value;
        var decision = booking.DecideCancellationSuccess(op.Id, op.Revision, evidence, Now);
        decision.Events.OfType<OrderCancelled>().ShouldBeEmpty();
        CancellationDecisionTests.Apply(booking, decision);
        booking.Status.ShouldBe(BookingStatus.Refunded);
        booking.RefundedAt.ShouldBe(refundTime);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Succeeded);
    }
}
