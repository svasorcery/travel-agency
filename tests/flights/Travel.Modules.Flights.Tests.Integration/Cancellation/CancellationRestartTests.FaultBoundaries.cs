using ErrorOr;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

public sealed partial class CancellationRestartTests
{
    [Fact]
    public async Task SlotClaimBeforeGetUsesLaterDeadline_without_get_or_user_request_after_restart()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        using (
            var hostA = await Start(
                postgres.GetConnectionString(),
                supplier,
                false,
                recoveryEnabled: false
            )
        )
        {
            var now = TimeProvider.System.GetUtcNow();
            var admission = Guid.NewGuid();
            var recovery = RecoverySchedule.Create(Guid.NewGuid(), now.AddSeconds(-301)).Value;
            var reserved = recovery.Reserve(3, now).UpdatedSchedule;
            using var scope = hostA.Services.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
            );
            session.Events.Append(
                id,
                new CancellationPreparationStarted(
                    operation,
                    owner,
                    "ord_fictional",
                    new string('a', 64),
                    new string('b', 64),
                    admission,
                    recovery.ClaimedAt
                ),
                new CancellationPreparationDispatched(
                    operation,
                    admission,
                    Guid.NewGuid(),
                    recovery,
                    recovery.ClaimedAt
                ),
                new CancellationObservationStarted(
                    operation,
                    recovery.Epoch,
                    3,
                    Guid.NewGuid(),
                    reserved,
                    now
                )
            );
            await session.SaveBookingWithWorkAsync(
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                id,
                CancellationWorkFactory.Recovery(id, operation, recovery),
                [],
                Ct
            );
            await hostA.StopAsync(Ct);
        }
        using (var hostB = await Start(postgres.GetConnectionString(), supplier, true))
        {
            var manual = await WaitState(
                hostB,
                id,
                b => b.CurrentCancellation?.Phase == CancellationPhase.ManualReviewRequired
            );
            manual.CurrentCancellation!.Recovery!.ConsumedMask.ShouldBe(15);
            supplier.PositiveReads.ShouldBe(0);
            supplier.CreateCalls.ShouldBe(0);
            await hostB.StopAsync(Ct);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentConfirmCancel_admission_blocks_loser_before_effect(
        bool confirmFirst
    )
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier();
        var money = new FictionalMoney();
        var booking = new GatedBooking();
        using var host = await Start(
            postgres.GetConnectionString(),
            supplier,
            false,
            configure: services =>
            {
                services.AddSingleton<IPaymentGateway>(money);
                services.AddSingleton<IFlightBookingProvider>(booking);
                services.AddSingleton<Travel.Modules.Flights.Application.Observability.IFlightsMetrics>(
                    Travel.Modules.Flights.Tests.Integration.Booking.NullFlightsMetricsImpl.Instance
                );
            }
        );
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await Seed(host, id, owner);
        using var scope = host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        if (confirmFirst)
        {
            var confirmation = bus.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                new ConfirmOrderCommand(id, owner),
                Ct
            );
            await booking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            try
            {
                var cancelled = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
                    new PrepareCancellationCommand(
                        id,
                        owner,
                        Guid.NewGuid(),
                        4,
                        new string('a', 64)
                    ),
                    Ct
                );
                cancelled.IsError.ShouldBeTrue();
                supplier.CreateCalls.ShouldBe(0);
                money.Authorizations.ShouldBe(0);
            }
            finally
            {
                booking.Release.TrySetResult();
            }
            (await confirmation).IsError.ShouldBeTrue();
        }
        else
        {
            var operation = Guid.NewGuid();
            (
                await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
                    new PrepareCancellationCommand(id, owner, operation, 3, new string('a', 64)),
                    Ct
                )
            ).IsError.ShouldBeFalse();
            var confirmed = await bus.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                new ConfirmOrderCommand(id, owner),
                Ct
            );
            confirmed.IsError.ShouldBeTrue();
            money.Authorizations.ShouldBe(0);
            booking.Preflights.ShouldBe(0);
        }
        money.Captures.ShouldBe(0);
        supplier.ConfirmCalls.ShouldBe(0);
        await host.StopAsync(Ct);
    }

    [Fact]
    public async Task LatePositiveAfterManualFinalizes_and_negative_without_quiescence_cannot_clear_live_sender()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier { ImmediatePositive = true, BlockConfirm = true };
        using var host = await Start(postgres.GetConnectionString(), supplier, false);
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        await Seed(host, id, owner);
        using var scope = host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        (
            await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
                new PrepareCancellationCommand(id, owner, operation, 3, new string('a', 64)),
                Ct
            )
        ).IsError.ShouldBeFalse();
        var ready = await WaitState(
            host,
            id,
            b => b.CurrentCancellation?.Phase == CancellationPhase.TermsReady
        );
        var op = ready.CurrentCancellation!;
        var terms = op.Terms!;
        (
            await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
                new ConsentCancellationCommand(
                    id,
                    owner,
                    op.Id,
                    op.Revision,
                    terms.Revision,
                    terms.Hash,
                    terms.NoticeVersion,
                    true,
                    new string('b', 64)
                ),
                Ct
            )
        ).IsError.ShouldBeFalse();
        await supplier.ConfirmEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        try
        {
            var store = host.Services.GetRequiredService<IDocumentStore>();
            await using (var escalation = store.LightweightSession())
            {
                var stream = await escalation.Events.FetchForWriting<BookingAggregate>(id, Ct);
                stream.AppendOne(
                    new CancellationManualReviewRequired(
                        operation,
                        CancellationUnknownStage.Confirmation,
                        CancellationReason.ManualVerificationRequired,
                        false,
                        TimeProvider.System.GetUtcNow()
                    )
                );
                await escalation.SaveChangesAsync(Ct);
            }
            var manual = await WaitState(
                host,
                id,
                b => b.CurrentCancellation?.Phase == CancellationPhase.ManualReviewRequired
            );
            op = manual.CurrentCancellation!;
            var negative = new ManualResolutionInput(
                id,
                ManualResolutionTargetKind.Cancellation,
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                ManualResolutionDecisionKind.ConfirmNoEffect,
                new(
                    "NOEFFECT-0001",
                    ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation,
                    TimeProvider.System.GetUtcNow(),
                    ProviderOrderRef: op.ProviderOrderRef,
                    SupplierFinalNoEffectsConfirmed: true
                )
            );
            var rejected = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
                new ResolveCancellationReviewCommand(new(Guid.NewGuid()), negative),
                Ct
            );
            rejected.IsError.ShouldBeTrue();
            (
                await WaitState(host, id, b => b.CurrentCancellation is not null)
            ).CurrentCancellation!.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        }
        finally
        {
            supplier.ConfirmRelease.TrySetResult();
        }
        var final = await WaitState(
            host,
            id,
            b => b.CurrentCancellation?.Outcome == CancellationOutcome.Succeeded
        );
        final.Status.ShouldBe(BookingStatus.Cancelled);
        supplier.ConfirmCalls.ShouldBe(1);
        await host.StopAsync(Ct);
    }

    private sealed class FictionalMoney : IPaymentGateway
    {
        public int Authorizations;
        public int Captures;

        public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
            Money amount,
            string key,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref Authorizations);
            return Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());
        }

        public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct)
        {
            Interlocked.Increment(ref Captures);
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<RefundRef>> RefundAsync(
            PaymentRef payment,
            Money amount,
            CancellationToken ct
        ) => throw new InvalidOperationException("No compensation expected.");
    }

    private sealed class GatedBooking : IFlightBookingProvider
    {
        public ProviderId Id => ProviderId.Duffel;
        public int Preflights;
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string order,
            Money money,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref Preflights);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            return Error.Conflict("Flights.OrderPriceChanged", "Fictional preflight mismatch.");
        }

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string reference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string order,
            PaymentRef payment,
            Money money,
            CancellationToken ct
        ) => throw new InvalidOperationException("No booking mutation expected.");

        public Task<ErrorOr<Success>> CancelOrderAsync(string order, CancellationToken ct) =>
            throw new InvalidOperationException("No legacy mutation expected.");

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
