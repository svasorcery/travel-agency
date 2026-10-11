using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Tests.Integration.Cancellation;
using Travel.Shared.Abstractions;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Refunds;

// Existing CI only: this fixture starts disposable PostgreSQL/store schema.
[Trait("Category", "Integration")]
public sealed class CustomerRefundPersistenceTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task One_claim_wins_and_new_session_never_admits_another_dispatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        var cancellation = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        using (var seed = fixture.Host.Services.CreateScope())
        {
            var s = seed.ServiceProvider.GetRequiredService<IDocumentSession>();
            var events = ProvenCancellation(id, owner, cancellation, now);
            s.Events.StartStream<BookingAggregate>(id, events.Cast<object>().ToArray());
            await s.SaveChangesAsync(ct);
            var stream = await s.Events.FetchForWriting<BookingAggregate>(id, ct);
            var admission = stream.Aggregate!.DecideCustomerRefundAdmission(
                owner,
                cancellation,
                refundId,
                now
            );
            admission.IsError.ShouldBeFalse();
            foreach (var e in admission.Value.Events)
                stream.AppendOne(e);
            await s.SaveChangesAsync(ct);
        }
        using var winner = fixture.Host.Services.CreateScope();
        using var loser = fixture.Host.Services.CreateScope();
        var first = winner.ServiceProvider.GetRequiredService<IDocumentSession>();
        var second = loser.ServiceProvider.GetRequiredService<IDocumentSession>();
        var a = await first.Events.FetchForWriting<BookingAggregate>(id, ct);
        var b = await second.Events.FetchForWriting<BookingAggregate>(id, ct);
        var sender = Guid.NewGuid();
        var one = a.Aggregate!.DecideCustomerRefundClaim(
            refundId,
            a.Aggregate.CustomerRefund!.Revision,
            sender,
            now
        );
        var two = b.Aggregate!.DecideCustomerRefundClaim(
            refundId,
            b.Aggregate.CustomerRefund!.Revision,
            Guid.NewGuid(),
            now
        );
        await CustomerRefundWriter.Persist(
            one.Value,
            a,
            first,
            winner.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            ct
        );
        await Should.ThrowAsync<BookingWriteConflictException>(() =>
            CustomerRefundWriter.Persist(
                two.Value,
                b,
                second,
                loser.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                ct
            )
        );
        using var fresh = fixture.Host.Services.CreateScope();
        var loaded = await fresh
            .ServiceProvider.GetRequiredService<IDocumentSession>()
            .Events.FetchForWriting<BookingAggregate>(id, ct);
        loaded.Aggregate!.CustomerRefund!.SenderId.ShouldBe(sender);
        loaded
            .Aggregate.DecideCustomerRefundClaim(
                refundId,
                loaded.Aggregate.CustomerRefund.Revision,
                Guid.NewGuid(),
                now.AddDays(1)
            )
            .Value.Events.Count.ShouldBe(0);
        var deadline = loaded.Aggregate.CustomerRefund.DeadlineAt;
        var expired = loaded.Aggregate.DecideCustomerRefundDeadline(refundId, deadline, deadline);
        await CustomerRefundWriter.Persist(
            expired.Value,
            loaded,
            fresh.ServiceProvider.GetRequiredService<IDocumentSession>(),
            fresh.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            ct
        );
        using var verify = fixture.Host.Services.CreateScope();
        var recovered = await verify
            .ServiceProvider.GetRequiredService<IDocumentSession>()
            .Events.FetchForWriting<BookingAggregate>(id, ct);
        recovered.Aggregate!.CustomerRefund!.State.ShouldBe(
            CustomerRefundState.ManualReviewRequired
        );
        recovered.Aggregate.CustomerRefund.DeadlineAt.ShouldBe(deadline);
    }

    internal static IDomainEvent[] ProvenCancellation(
        Guid id,
        Guid owner,
        Guid cancellation,
        DateTimeOffset now
    )
    {
        var money = Money.Create(100m, CurrencyCode.Create("USD").Value).Value;
        var refund = Money.Create(80m, CurrencyCode.Create("USD").Value).Value;
        var payment = PaymentRef.New();
        var attempt = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var held = CancellationTestFixture.HeldEvents(owner, now.AddSeconds(-10));
        var terms = CancellationTerms
            .Create(
                new(
                    1,
                    id,
                    owner,
                    cancellation,
                    "ord_fictional",
                    "ore_fictional",
                    new string('a', 64),
                    refund,
                    CancellationRefundDestination.Balance,
                    new(
                        SettlementComposition.CashOnly,
                        true,
                        CancellationResolutionSource.SupplierApi,
                        false,
                        false
                    ),
                    now.AddMinutes(10),
                    "cancellation-v1"
                ),
                now
            )
            .Value;
        var evidence = CancellationEvidence
            .FromSupplierConfirmation(
                terms,
                new(
                    "ord_fictional",
                    "ore_fictional",
                    new string('a', 64),
                    refund,
                    terms.Destination,
                    terms.Settlement,
                    now.AddSeconds(-1),
                    now,
                    CancellationResolutionSource.SupplierApi
                ),
                now
            )
            .Value;
        return held.Concat<IDomainEvent>([
                new ConfirmationAttemptStarted(
                    attempt,
                    owner,
                    admission,
                    new string('c', 64),
                    "ord_fictional",
                    money,
                    now.AddSeconds(-9)
                ),
                new ConfirmationEffectsClaimed(attempt, admission, sender, now.AddSeconds(-8)),
                new ConfirmationPaymentReferenceRecorded(attempt, payment, now.AddSeconds(-8)),
                new ConfirmationCaptureObserved(attempt, payment, money, now.AddSeconds(-7)),
                new ConfirmationAttemptCompleted(
                    attempt,
                    payment,
                    money,
                    "ord_fictional",
                    SupplierPaymentEvidence
                        .Create("pay_fictional", money, SupplierPaymentKind.Balance)
                        .Value,
                    CancellationResolutionSource.SupplierApi,
                    now.AddSeconds(-6)
                ),
                new OrderConfirmed("ord_fictional", payment, now.AddSeconds(-6)),
                new CancellationPreparationStarted(
                    cancellation,
                    owner,
                    "ord_fictional",
                    new string('a', 64),
                    new string('p', 64),
                    Guid.NewGuid(),
                    now.AddSeconds(-5)
                ),
                new CancellationTermsObtained(cancellation, terms, now.AddSeconds(-4)),
                new CancellationTermsAccepted(
                    cancellation,
                    1,
                    terms.Hash,
                    terms.NoticeVersion,
                    new string('d', 64),
                    Guid.NewGuid(),
                    now.AddSeconds(-3)
                ),
                new CancellationSucceeded(cancellation, evidence, now),
                new OrderCancelled(CancelReason.User, now.AddSeconds(-1)),
            ])
            .ToArray();
    }
}
