using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationStatusTests
{
    [Fact]
    public void Api_terms_route_uses_existing_flat_journey_contract()
    {
        var now = CancellationTestData.Now;
        var segment = Travel
            .Modules.Flights.Core.ValueObjects.Segment.Create(
                Travel.Modules.Flights.Core.ValueObjects.IataCode.Create("LED").Value,
                Travel.Modules.Flights.Core.ValueObjects.IataCode.Create("DME").Value,
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                "SU",
                "100",
                Travel.Modules.Flights.Core.ValueObjects.CabinClass.Economy
            )
            .Value;
        var itinerary = Travel
            .Modules.Flights.Core.ValueObjects.Itinerary.Create([
                Travel.Modules.Flights.Core.ValueObjects.Slice.Create([segment]).Value,
            ])
            .Value;
        var booking = CancellationDecisionTests.Prepared();
        typeof(Travel.Modules.Flights.Core.Aggregates.BookingAggregate)
            .GetProperty("Itinerary")!
            .SetValue(booking, itinerary);
        var status = CancellationStatusFactory.Create(booking, booking.Version, null, now);
        var api = Travel.Modules.Flights.Api.Contracts.CancellationStatusResponse.From(status);
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(api, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        var route = json
            .RootElement.GetProperty("operation")
            .GetProperty("terms")
            .GetProperty("wholeOrderItinerary");
        route.GetProperty("totalDuration").ValueKind.ShouldBe(JsonValueKind.String);
        route.GetProperty("slices")[0].GetProperty("origin").GetString().ShouldBe("LED");
        route
            .GetProperty("slices")[0]
            .GetProperty("segments")[0]
            .GetProperty("cabinClass")
            .GetString()
            .ShouldBe("economy");
    }

    [Fact]
    public void Terminal_cancellation_cannot_hide_active_confirmation_review_target()
    {
        var booking = CancellationDecisionTests.Prepared();
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationAbandon(
                op.OwnerId,
                op.Id,
                op.Revision,
                CancellationTestData.Now
            )
        );
        var attempt = Guid.NewGuid();
        booking.Apply(
            new ConfirmationAttemptStarted(
                attempt,
                op.OwnerId,
                Guid.NewGuid(),
                new string('a', 64),
                op.ProviderOrderRef,
                CancellationTestData.Money(),
                CancellationTestData.Now
            )
        );
        var review = CancellationReviewFactory.Create(
            booking,
            booking.Version,
            CancellationTestData.Now
        );
        review.Target!.Kind.ShouldBe("Confirmation");
        review.Target.TargetId.ShouldBe(attempt);
        review.Target.AcceptedAmount.ShouldNotBeNull();
        review.Status.Operation!.Phase.ShouldBe("Abandoned");
    }

    [Fact]
    public void Completed_confirmation_cannot_hide_active_cancellation_review_target()
    {
        var booking = CancellationDecisionTests.Prepared();
        var op = booking.CurrentCancellation!;
        var attempt = Guid.NewGuid();
        var payment = Travel.Modules.Flights.Core.ValueObjects.Identifiers.PaymentRef.New();
        booking.Apply(
            new ConfirmationAttemptStarted(
                attempt,
                op.OwnerId,
                Guid.NewGuid(),
                new string('a', 64),
                op.ProviderOrderRef,
                CancellationTestData.Money(),
                CancellationTestData.Now
            )
        );
        booking.Apply(
            new ConfirmationAttemptCompleted(
                attempt,
                payment,
                CancellationTestData.Money(),
                op.ProviderOrderRef,
                Travel
                    .Modules.Flights.Core.Providers.Dtos.SupplierPaymentEvidence.Create(
                        "pay_fictional",
                        CancellationTestData.Money(),
                        Travel.Modules.Flights.Core.Providers.Dtos.SupplierPaymentKind.Balance
                    )
                    .Value,
                CancellationResolutionSource.SupplierApi,
                CancellationTestData.Now
            )
        );
        var review = CancellationReviewFactory.Create(
            booking,
            booking.Version,
            CancellationTestData.Now
        );
        review.Target!.Kind.ShouldBe("Cancellation");
        review.Target.TargetId.ShouldBe(op.Id);
        review.Target.ItineraryPartyHash.ShouldBe(op.ItineraryPartyHash);
    }

    [Fact]
    public void Manual_operation_with_accepted_refresh_exposes_pending_read()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var now = CancellationTestData.Now.AddSeconds(311);
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRecoveryDeadline(op.Id, op.Recovery!.Epoch, now)
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                new string('f', 64),
                now
            )
        );
        var status = CancellationStatusFactory.Create(booking, booking.Version, null, now);
        status.Operation!.Phase.ShouldBe("ManualReviewRequired");
        status.Operation.ReadPending.ShouldBeTrue();
    }

    [Fact]
    public void Owner_status_contains_current_terms_but_no_private_supplier_process_or_payment_refs()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var status = CancellationStatusFactory.Create(
            booking,
            booking.Version,
            op.Id,
            CancellationTestData.Now
        );
        status.Operation!.OperationId.ShouldBe(op.Id);
        status.Operation.Terms!.RefundAmount.ShouldBe("17.25");
        status.Operation.Terms.RefundCurrency.ShouldBe("USD");
        var json = JsonSerializer.Serialize(status);
        json.ShouldNotContain(op.ProviderOrderRef);
        json.ShouldNotContain(op.Terms!.ProviderCancellationRef);
        json.ShouldNotContain(op.DispatchOwnerInstanceId!.Value.ToString());
        json.ShouldNotContain("PaymentReference");
        json.ShouldNotContain("Quiescence");
    }

    [Fact]
    public void Historical_retry_is_distinguished_from_current_operation()
    {
        var booking = CancellationDecisionTests.Prepared();
        var old = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationAbandon(
                old.OwnerId,
                old.Id,
                old.Revision,
                CancellationTestData.Now
            )
        );
        var next = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationPrepare(
                old.OwnerId,
                next,
                booking.Version,
                new string('f', 64),
                Guid.NewGuid(),
                CancellationTestData.Now,
                old.ItineraryPartyHash
            )
        );
        var status = CancellationStatusFactory.Create(
            booking,
            booking.Version,
            old.Id,
            CancellationTestData.Now
        );
        status.RequestedOperationId.ShouldBe(old.Id);
        status.CurrentOperationId.ShouldBe(next);
        status.IsCurrentOperation.ShouldBeFalse();
        status.Operation!.Phase.ShouldBe("Abandoned");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Confirmation_or_legacy_blocker_is_visible_without_cancellation_operation(
        bool coordinated
    )
    {
        var booking = coordinated
            ? ConfirmationBarrierTests.Started()
            : CancellationDecisionTests.Held(false);
        var status = CancellationStatusFactory.Create(
            booking,
            booking.Version,
            null,
            CancellationTestData.Now
        );
        status.Operation.ShouldBeNull();
        status.BlockingConfirmation.ShouldNotBeNull();
        status.BlockingConfirmation.Kind.ShouldBe(
            coordinated ? "ConfirmationAttempt" : "LegacyHeld"
        );
        status.BlockingConfirmation.TargetId.ShouldBe(
            coordinated ? booking.CurrentConfirmationAttempt!.Id : booking.Id
        );
    }

    [Fact]
    public void Recovery_work_keeps_four_fixed_slots_and_deadline_for_saved_epoch()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var work = CancellationWorkFactory.Recovery(booking.Id, op.Id, op.Recovery!);
        work.Count.ShouldBe(5);
        work.Take(4)
            .Select(item => ((ObserveCancellation)item.Message).Slot)
            .ShouldBe([0, 1, 2, 3]);
        work.Take(4)
            .Select(item => ((ObserveCancellation)item.Message).RecoveryEpoch)
            .ShouldAllBe(epoch => epoch == op.Recovery!.Epoch);
        work.Select(item => item.DueAt!.Value - op.Recovery!.ClaimedAt)
            .ShouldBe(
                new[] { 2, 10, 60, 300, 310 }.Select(seconds => TimeSpan.FromSeconds(seconds))
            );
        work[4].Message.ShouldBeOfType<RecoveryDeadline>();
    }
}
