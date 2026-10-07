using Marten;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

[Trait("Category", "Integration")]
public sealed class CreationManualReviewTests(AncillaryMartenFixture fixture)
    : IClassFixture<AncillaryMartenFixture>
{
    [Fact]
    public async Task Before_wallet_difference_and_unclaimed_financial_close_share_one_atomic_batch()
    {
        var id = await fixture.Quote();
        var ct = TestContext.Current.CancellationToken;
        BookedOrderFacts matched;
        await using (var setup = fixture.Store.LightweightSession())
        {
            var stream = await setup.Events.FetchForWriting<BookingAggregate>(id, ct);
            var b = stream.Aggregate!;
            var start = AncillaryMartenFixture.Start(b);
            matched = new(
                "ord_verified",
                b.ProviderOfferRef!,
                b.Itinerary!,
                new(b.QuoteBinding!.Slots.Select(s => s.Id.Value).ToArray()),
                start.Accepted.Services,
                start.Accepted.Total,
                true,
                AncillaryMartenFixture.Now.AddHours(2)
            );
            stream.AppendOne(start);
            stream.AppendOne(
                new BookingCreationObserved(
                    start.AttemptId,
                    new(
                        BookingCreationOutcome.Matches,
                        matched,
                        matched.ProviderOrderId,
                        true,
                        true,
                        "OrderObserved",
                        AncillaryMartenFixture.Now
                    ),
                    AncillaryMartenFixture.Now
                )
            );
            stream.AppendOne(
                new OfferHeldV3(
                    matched.ProviderOrderId,
                    start.ProtectedParty,
                    matched.PaymentRequiredBy,
                    AncillaryMartenFixture.Now,
                    start.OwnerId,
                    start.QuoteRevision,
                    1
                )
            );
            stream.AppendOne(new BookingMutationCoordinationEnabled(AncillaryMartenFixture.Now));
            stream.AppendOne(
                new ConfirmationAttemptStarted(
                    Guid.NewGuid(),
                    start.OwnerId,
                    Guid.NewGuid(),
                    new string('a', 64),
                    matched.ProviderOrderId,
                    matched.Total,
                    AncillaryMartenFixture.Now
                )
            );
            await setup.SaveChangesAsync(ct);
        }
        var changed = matched with
        {
            Services = new([]),
            Total = Travel
                .Modules.Flights.Core.ValueObjects.Money.Create(50, matched.Total.Currency)
                .Value,
        };
        fixture.Failure.Enabled = true;
        try
        {
            await using var failed = fixture.Store.LightweightSession();
            var stream = await failed.Events.FetchForWriting<BookingAggregate>(id, ct);
            var decision = stream.Aggregate!.DecideConfirmationServiceDifference(
                changed,
                AncillaryMartenFixture.Now
            );
            decision.Kind.ShouldBe(CancellationDecisionKind.Allowed);
            foreach (var e in decision.Events)
                stream.AppendOne(e);
            await Should.ThrowAsync<IOException>(() => failed.SaveChangesAsync(ct));
        }
        finally
        {
            fixture.Failure.Enabled = false;
        }
        await using (var retry = fixture.Store.LightweightSession())
        {
            var stream = await retry.Events.FetchForWriting<BookingAggregate>(id, ct);
            stream.Aggregate!.CurrentCreation!.Outcome.ShouldBe(BookingCreationOutcome.Matches);
            stream.Aggregate.HasConfirmationBarrier.ShouldBeTrue();
            foreach (
                var e in stream
                    .Aggregate.DecideConfirmationServiceDifference(
                        changed,
                        AncillaryMartenFixture.Now
                    )
                    .Events
            )
                stream.AppendOne(e);
            await retry.SaveChangesAsync(ct);
        }
        await using var fresh = fixture.Store.QuerySession();
        var current = (await fresh.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct))!;
        current.CurrentCreation!.Outcome.ShouldBe(BookingCreationOutcome.CreatedWithDifferences);
        current.HasConfirmationBarrier.ShouldBeFalse();
        current.TotalAmount!.Amount.ShouldBe(50);
        current.CurrentCreation.Accepted.Total.Amount.ShouldBe(70);
        (await fresh.Events.FetchStreamAsync(id, token: ct))
            .Any(e => e.Data is ConfirmationCaptureObserved)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Restarted_unknown_accepts_typed_operator_difference_and_preserves_original_purchase()
    {
        var id = await fixture.Quote();
        var ct = TestContext.Current.CancellationToken;
        BookingCreationStarted start;
        await using (var first = fixture.Store.LightweightSession())
        {
            var stream = await first.Events.FetchForWriting<BookingAggregate>(id, ct);
            start = AncillaryMartenFixture.Start(stream.Aggregate!);
            stream.AppendOne(start);
            await first.SaveChangesAsync(ct);
        }
        await using (var second = fixture.Store.LightweightSession())
        {
            var stream = await second.Events.FetchForWriting<BookingAggregate>(id, ct);
            var booking = stream.Aggregate!;
            var observed = booking
                .DecideCreationObservation(
                    start.AttemptId,
                    1,
                    new(
                        BookingCreationOutcome.ManualReviewRequired,
                        null,
                        null,
                        false,
                        false,
                        "SenderLost",
                        start.OccurredAt
                    ),
                    start.OccurredAt
                )
                .Value.Event!;
            stream.AppendOne(observed);
            await second.SaveChangesAsync(ct);
        }
        await using (var operatorSession = fixture.Store.LightweightSession())
        {
            var stream = await operatorSession.Events.FetchForWriting<BookingAggregate>(id, ct);
            var b = stream.Aggregate!;
            var attempt = b.CurrentCreation!;
            var actual = new BookedOrderFacts(
                "ord_verified",
                b.ProviderOfferRef!,
                b.Itinerary!,
                new(b.QuoteBinding!.Slots.Select(s => s.Id.Value).ToArray()),
                new([]),
                attempt.Accepted.BaseFare,
                true,
                AncillaryMartenFixture.Now.AddHours(2)
            );
            var evidence = new ManualResolutionEvidence(
                "SUPPORT-001",
                ManualEvidenceCategory.SupplierSupportAttestation,
                AncillaryMartenFixture.Now.AddMinutes(5),
                ProviderOrderRef: actual.ProviderOrderId,
                QuiescenceRef: "STOPPED-001",
                StoppedAt: AncillaryMartenFixture.Now.AddMinutes(4),
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
            var decision = b.DecideManualResolution(
                Guid.NewGuid(),
                new(
                    id,
                    ManualResolutionTargetKind.Creation,
                    attempt.Id,
                    attempt.Revision,
                    Guid.NewGuid(),
                    ManualResolutionDecisionKind.AttachDifferences,
                    evidence
                ),
                evidence.ObservedAt
            );
            decision.Kind.ShouldBe(CancellationDecisionKind.Allowed);
            foreach (var e in decision.Events)
                stream.AppendOne(e);
            await operatorSession.SaveChangesAsync(ct);
        }
        await using var restarted = fixture.Store.QuerySession();
        var current = (
            await restarted.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct)
        )!;
        current.Status.ShouldBe(BookingStatus.Held);
        current.TotalAmount!.Amount.ShouldBe(50);
        current.CurrentCreation!.Accepted.Total.Amount.ShouldBe(70);
        current.CreationBlocksConfirmation.ShouldBeTrue();
        current.HasUnresolvedCreation.ShouldBeFalse();
        current.ManualReviewHistory.Count.ShouldBe(1);
    }
}
