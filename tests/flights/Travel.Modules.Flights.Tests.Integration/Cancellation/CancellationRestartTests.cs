using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Cancellation;
using Wolverine;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

// Source-compiled locally; actual production consumers and schema-backed runtime are existing-CI only.
[Trait("Category", "Integration")]
public sealed partial class CancellationRestartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LostConfirmResponseRestoresSuccess_after_host_replacement_without_user_get_or_republish()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        using (var hostA = await Start(postgres.GetConnectionString(), supplier, false))
        {
            await Seed(hostA, id, owner);
            using var scope = hostA.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var prepared = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
                new PrepareCancellationCommand(id, owner, operation, 3, new string('a', 64)),
                Ct
            );
            prepared.IsError.ShouldBeFalse();
            var ready = await WaitState(
                hostA,
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
                        operation,
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
            await WaitState(hostA, id, b => b.CurrentCancellation?.HadConfirmationUnknown == true);
            await hostA.StopAsync(Ct);
        }
        var readsBefore = supplier.PositiveReads;
        using (var hostB = await Start(postgres.GetConnectionString(), supplier, true))
        {
            var completed = await WaitState(
                hostB,
                id,
                b => b.CurrentCancellation?.Outcome == CancellationOutcome.Succeeded
            );
            completed.Status.ShouldBe(BookingStatus.Cancelled);
            completed.CurrentCancellation!.Evidence!.Refund.ShouldBe(Refund());
            supplier.CreateCalls.ShouldBe(1);
            supplier.ConfirmCalls.ShouldBe(1);
            supplier.PositiveReads.ShouldBeGreaterThan(readsBefore);
            await hostB.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task ClaimBeforeSendRestartStaysSafe_and_duplicate_workers_never_create_again()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var claimedAt = TimeProvider.System.GetUtcNow();
        var recovery = RecoverySchedule.Create(Guid.NewGuid(), claimedAt).Value;
        using (
            var hostA = await Start(
                postgres.GetConnectionString(),
                supplier,
                false,
                recoveryEnabled: false
            )
        )
        {
            using var scope = hostA.Services.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, claimedAt).Cast<object>().ToArray()
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
                    claimedAt
                ),
                new CancellationPreparationDispatched(
                    operation,
                    admission,
                    Guid.NewGuid(),
                    recovery,
                    claimedAt
                )
            );
            await session.SaveBookingWithWorkAsync(
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                id,
                CancellationWorkFactory.Recovery(id, operation, recovery),
                [],
                Ct
            );
            var runtime = hostA.Services.GetRequiredService<Wolverine.Runtime.IWolverineRuntime>();
            var persisted = (await runtime.Storage.Admin.AllOutgoingAsync())
                .Concat(await runtime.Storage.Admin.AllIncomingAsync())
                .ToArray();
            persisted
                .DistinctBy(envelope => envelope.Id)
                .Count(envelope => envelope.MessageType == typeof(ObserveCancellation).FullName)
                .ShouldBe(4);
            (
                await WaitState(hostA, id, b => b.CurrentCancellation is not null)
            ).CurrentCancellation!.Phase.ShouldBe(CancellationPhase.Preparing);
            await hostA.StopAsync(Ct);
        }
        using (var hostB = await Start(postgres.GetConnectionString(), supplier, true))
        {
            using var scope = hostB.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new ExecuteCancellationPreparation(id, operation, admission));
            await bus.PublishAsync(new ExecuteCancellationPreparation(id, operation, admission));
            var manual = await WaitState(
                hostB,
                id,
                b => b.CurrentCancellation?.Phase == CancellationPhase.ManualReviewRequired
            );
            manual.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Unknown);
            supplier.CreateCalls.ShouldBe(0);
            supplier.ConfirmCalls.ShouldBe(0);
            supplier.PositiveReads.ShouldBeGreaterThan(0);
            await hostB.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task DuplicateWorkersDoNotResend_after_confirmation_dispatch_claim()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier();
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
        var unknown = await WaitState(
            host,
            id,
            b => b.CurrentCancellation?.HadConfirmationUnknown == true
        );
        op = unknown.CurrentCancellation!;
        await bus.InvokeAsync(
            new ExecuteCancellationConfirmation(id, op.Id, op.ConfirmationAdmissionId!.Value),
            Ct
        );
        await bus.InvokeAsync(
            new ExecuteCancellationConfirmation(id, op.Id, op.ConfirmationAdmissionId.Value),
            Ct
        );
        supplier.CreateCalls.ShouldBe(1);
        supplier.ConfirmCalls.ShouldBe(1);
        await host.StopAsync(Ct);
    }

    [Fact]
    public async Task ExternalSuccessLocalRollbackRecovers_via_saved_observation_work_without_resending_confirm()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var supplier = new FictionalSupplier { ImmediatePositive = true };
        var failure = new OneFinalizationFailure();
        using var host = await Start(postgres.GetConnectionString(), supplier, true, failure);
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
        var completed = await WaitState(
            host,
            id,
            b => b.CurrentCancellation?.Outcome == CancellationOutcome.Succeeded
        );
        failure.Failures.ShouldBe(1);
        supplier.ConfirmCalls.ShouldBe(1);
        supplier.PositiveReads.ShouldBeGreaterThan(0);
        completed.Status.ShouldBe(BookingStatus.Cancelled);
        await using var read = host
            .Services.GetRequiredService<IDocumentStore>()
            .LightweightSession();
        var events = await read.Events.FetchStreamAsync(id, token: Ct);
        events.Count(e => e.Data is CancellationSucceeded).ShouldBe(1);
        events.Count(e => e.Data is OrderCancelled).ShouldBe(1);
        await host.StopAsync(Ct);
    }

    private sealed class OneFinalizationFailure : DocumentSessionListenerBase
    {
        public int Failures;

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken ct)
        {
            if (
                session
                    .PendingChanges.Streams()
                    .Any(stream => stream.Events.Any(e => e.Data is CancellationSucceeded))
                && Interlocked.CompareExchange(ref Failures, 1, 0) == 0
            )
                throw new IOException("Synthetic local finalization failure before commit.");
            return Task.CompletedTask;
        }
    }

    private static async Task<IHost> Start(
        string connection,
        FictionalSupplier supplier,
        bool recoveryHost,
        OneFinalizationFailure? failure = null,
        bool recoveryEnabled = true,
        Action<IServiceCollection>? configure = null
    )
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDispatchInstanceIdentity, ProcessDispatchInstanceIdentity>();
        builder.Services.AddSingleton<IFlightCancellationProvider>(
            new HostSupplier(supplier, recoveryHost)
        );
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(connection);
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
                if (failure is not null)
                    options.Listeners.Add(failure);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            foreach (
                var handler in new[]
                {
                    typeof(PrepareCancellationHandler),
                    typeof(ConsentCancellationHandler),
                    typeof(ResolveCancellationReviewHandler),
                    typeof(Travel.Modules.Flights.Application.Handlers.Booking.ConfirmOrderHandler),
                    typeof(ExecuteCancellationPreparationHandler),
                    typeof(ExecuteCancellationConfirmationHandler),
                    typeof(ObserveCancellationHandler),
                    typeof(CancellationAdmissionDeadlineHandler),
                    typeof(CancellationRecoveryDeadlineHandler),
                    typeof(ConfirmationBarrierDeadlineHandler),
                    typeof(CancellationReconcileProbeHandler),
                }
            )
                options.Discovery.IncludeType(handler);
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            CancellationDeliveryPolicy.Configure(options);
            options.Services.RunWolverineInSoloMode();
            options.Durability.DurabilityAgentEnabled = recoveryEnabled;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
        });
        if (configure is null)
        {
            builder.Services.AddSingleton<IPaymentGateway>(new FictionalMoney());
            builder.Services.AddSingleton<IFlightBookingProvider>(new GatedBooking());
            builder.Services.AddSingleton<Travel.Modules.Flights.Application.Observability.IFlightsMetrics>(
                Travel.Modules.Flights.Tests.Integration.Booking.NullFlightsMetricsImpl.Instance
            );
        }
        configure?.Invoke(builder.Services);
        var host = builder.Build();
        if (!recoveryEnabled)
            await host
                .Services.GetRequiredService<Wolverine.Runtime.IWolverineRuntime>()
                .Storage.Admin.MigrateAsync();
        await host.StartAsync(Ct);
        return host;
    }

    private static async Task Seed(IHost host, Guid id, Guid owner)
    {
        await using var session = host
            .Services.GetRequiredService<IDocumentStore>()
            .LightweightSession();
        session.Events.StartStream<BookingAggregate>(
            id,
            CancellationTestFixture
                .HeldEvents(owner, TimeProvider.System.GetUtcNow())
                .Cast<object>()
                .ToArray()
        );
        await session.SaveChangesAsync(Ct);
    }

    // Polls stored stream only. It does not invoke owner status, supplier read, refresh or republish.
    private static async Task<BookingAggregate> WaitState(
        IHost host,
        Guid id,
        Func<BookingAggregate, bool> predicate
    )
    {
        var until = TimeProvider.System.GetUtcNow().AddSeconds(45);
        while (TimeProvider.System.GetUtcNow() < until)
        {
            await using var session = host
                .Services.GetRequiredService<IDocumentStore>()
                .LightweightSession();
            var loaded = await session.Events.FetchForWriting<BookingAggregate>(id, Ct);
            if (loaded.Aggregate is { } booking && predicate(booking))
                return booking;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException(
            "Fictional persisted cancellation did not reach the expected state."
        );
    }

    private static Money Refund() => Money.Create(17.25m, CurrencyCode.Create("USD").Value).Value;

    private sealed class FictionalSupplier
    {
        public int CreateCalls;
        public int ConfirmCalls;
        public int PositiveReads;
        public DateTimeOffset? ConfirmedAt;
        public bool ImmediatePositive;
        public bool BlockConfirm;
        public TaskCompletionSource ConfirmEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConfirmRelease { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class HostSupplier(FictionalSupplier state, bool recoveryHost)
        : IFlightCancellationProvider
    {
        public Task<ErrorOr<CancellationEligibility>> InspectOrderAsync(
            string order,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<CancellationEligibility>>(
                new CancellationEligibility(
                    order,
                    true,
                    CancellationProviderPaymentState.Paid,
                    Money.Create(100m, CurrencyCode.Create("USD").Value).Value,
                    new(
                        SettlementComposition.Unknown,
                        false,
                        CancellationResolutionSource.SupplierApi,
                        false,
                        false
                    )
                )
            );

        public Task<ErrorOr<CancellationQuoteResult>> CreateTermsAsync(
            string order,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref state.CreateCalls);
            var now = TimeProvider.System.GetUtcNow();
            return Task.FromResult<ErrorOr<CancellationQuoteResult>>(
                new CancellationQuoteResult(
                    CancellationQuoteOutcome.TermsAvailable,
                    new(
                        order,
                        "occ_fictional",
                        Refund(),
                        CancellationRefundDestination.Balance,
                        new(
                            SettlementComposition.CashOnly,
                            true,
                            CancellationResolutionSource.SupplierApi,
                            false,
                            false
                        ),
                        now.AddMinutes(10),
                        now
                    )
                )
            );
        }

        public async Task<ErrorOr<CancellationEffectResult>> ConfirmAsync(
            CancellationTerms terms,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref state.ConfirmCalls);
            if (state.BlockConfirm)
            {
                state.ConfirmEntered.TrySetResult();
                await state.ConfirmRelease.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            state.ConfirmedAt = TimeProvider.System.GetUtcNow();
            if (state.ImmediatePositive)
            {
                var now = TimeProvider.System.GetUtcNow();
                var observed = new CancellationObservation(
                    terms.ProviderOrderRef,
                    terms.ProviderCancellationRef,
                    CancellationObservationState.Confirmed,
                    new CancellationQuoteFacts(
                        terms.ProviderOrderRef,
                        terms.ProviderCancellationRef,
                        terms.Refund,
                        terms.Destination,
                        terms.Settlement,
                        terms.ExpiresAt,
                        now,
                        terms.ItineraryPartyHash
                    ),
                    state.ConfirmedAt,
                    now,
                    CancellationResolutionSource.SupplierApi
                );
                return await Task.FromResult<ErrorOr<CancellationEffectResult>>(
                    new CancellationEffectResult(CancellationEffectOutcome.Confirmed, observed)
                );
            }
            return await Task.FromResult<ErrorOr<CancellationEffectResult>>(
                new CancellationEffectResult(
                    CancellationEffectOutcome.Unknown,
                    null,
                    CancellationReason.ProviderUnavailable
                )
            );
        }

        public Task<ErrorOr<CancellationObservation>> ObserveAsync(
            CancellationCorrelation correlation,
            CancellationToken ct
        )
        {
            var now = TimeProvider.System.GetUtcNow();
            if (recoveryHost)
                Interlocked.Increment(ref state.PositiveReads);
            if (
                recoveryHost
                && state.ConfirmedAt is { } confirmed
                && correlation.AcceptedTerms is { } terms
            )
                return Task.FromResult<ErrorOr<CancellationObservation>>(
                    new CancellationObservation(
                        correlation.ProviderOrderRef,
                        correlation.ProviderCancellationRef,
                        CancellationObservationState.Confirmed,
                        new(
                            terms.ProviderOrderRef,
                            terms.ProviderCancellationRef,
                            terms.Refund,
                            terms.Destination,
                            terms.Settlement,
                            terms.ExpiresAt,
                            now,
                            terms.ItineraryPartyHash
                        ),
                        confirmed,
                        now,
                        CancellationResolutionSource.SupplierApi
                    )
                );
            return Task.FromResult<ErrorOr<CancellationObservation>>(
                new CancellationObservation(
                    correlation.ProviderOrderRef,
                    correlation.ProviderCancellationRef,
                    CancellationObservationState.Unknown,
                    null,
                    null,
                    now,
                    CancellationResolutionSource.SupplierApi,
                    correlation.ProviderCancellationRef is null
                        ? CancellationReason.Uncorrelated
                        : CancellationReason.Pending
                )
            );
        }
    }
}
