using System.Reflection;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

[Trait("Category", "Integration")]
public sealed class CancellationRecoveryTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task Observation_reservation_is_committed_before_get_and_duplicate_slot_never_gets_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using (var seed = fixture.Host.Services.CreateScope())
        {
            var session = seed.ServiceProvider.GetRequiredService<IDocumentSession>();
            var initial = CancellationTestFixture.HeldEvents(owner, now.AddSeconds(-3));
            session.Events.StartStream<BookingAggregate>(id, initial.Cast<object>().ToArray());
            session.Events.Append(
                id,
                new CancellationPreparationStarted(
                    opId,
                    owner,
                    "ord_fictional",
                    new string('a', 64),
                    new string('b', 64),
                    admission,
                    now.AddSeconds(-3)
                ),
                new CancellationPreparationDispatched(
                    opId,
                    admission,
                    Guid.NewGuid(),
                    RecoverySchedule.Create(epoch, now.AddSeconds(-3)).Value,
                    now.AddSeconds(-3)
                )
            );
            await session.SaveChangesAsync(ct);
        }
        var store = fixture.Host.Services.GetRequiredService<IDocumentStore>();
        var probe = new ReadProbe(store, id, opId);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var scope = fixture.Host.Services.CreateScope();
            await ObserveCancellationHandler.Handle(
                new(id, opId, epoch, 0),
                scope.ServiceProvider.GetRequiredService<IDocumentSession>(),
                store,
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                probe,
                TimeProvider.System,
                ct
            );
        }
        probe.Reads.ShouldBe(1);
        await using var verify = store.LightweightSession();
        var loaded = await verify.Events.FetchForWriting<BookingAggregate>(id, ct);
        loaded.Aggregate!.CurrentCancellation!.Recovery!.ConsumedMask.ShouldBe(1);
        loaded.Aggregate.CurrentCancellation.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
    }

    [Fact]
    public async Task Saved_last_read_window_expiring_during_commit_never_starts_a_supplier_get()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var op = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var claimedAt = TimeProvider.System.GetUtcNow();
        var store = fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, claimedAt).Cast<object>().ToArray()
            );
            seed.Events.Append(
                id,
                new CancellationPreparationStarted(
                    op,
                    owner,
                    "ord_fictional",
                    new string('a', 64),
                    new string('b', 64),
                    admission,
                    claimedAt
                ),
                new CancellationPreparationDispatched(
                    op,
                    admission,
                    Guid.NewGuid(),
                    RecoverySchedule.Create(epoch, claimedAt).Value,
                    claimedAt
                )
            );
            await seed.SaveChangesAsync(ct);
        }
        var time = new FakeTimeProvider(claimedAt.AddSeconds(309));
        var probe = new ReadProbe(store, id, op);
        using var scope = fixture.Host.Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var delayed = DispatchProxy.Create<IDocumentSession, DelayedCommit>();
        var interceptor = (DelayedCommit)(object)delayed;
        interceptor.Inner = actual;
        interceptor.Time = time;
        await ObserveCancellationHandler.Handle(
            new(id, op, epoch, 3),
            delayed,
            store,
            scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            probe,
            time,
            ct
        );
        probe.Reads.ShouldBe(0);
        await using var verify = store.LightweightSession();
        var loaded = await verify.Events.FetchForWriting<BookingAggregate>(id, ct);
        loaded.Aggregate!.CurrentCancellation!.Recovery!.ConsumedMask.ShouldBe(15);
        loaded.Aggregate.CurrentCancellation.Recovery.ReadWindowUntil.ShouldBe(
            claimedAt.AddSeconds(310)
        );
        time.GetUtcNow().ShouldBe(claimedAt.AddSeconds(311));
    }

    public class DelayedCommit : DispatchProxy
    {
        internal IDocumentSession Inner { get; set; } = default!;
        internal FakeTimeProvider Time { get; set; } = default!;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            var result = method!.Invoke(Inner, arguments);
            return method.Name == "SaveChangesAsync" ? After((Task)result!) : result;
        }

        private async Task After(Task saved)
        {
            await saved;
            Time.Advance(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class ReadProbe(IDocumentStore store, Guid aggregateId, Guid operationId)
        : IFlightCancellationProvider
    {
        public int Reads { get; private set; }

        public async Task<ErrorOr.ErrorOr<CancellationObservation>> ObserveAsync(
            CancellationCorrelation correlation,
            CancellationToken ct
        )
        {
            Reads++;
            await using var session = store.LightweightSession();
            var loaded = await session.Events.FetchForWriting<BookingAggregate>(aggregateId, ct);
            var operation = loaded.Aggregate!.CancellationOperations[operationId];
            operation.ActiveReadId.ShouldNotBeNull();
            operation.Recovery!.ConsumedMask.ShouldBe(1);
            return new CancellationObservation(
                correlation.ProviderOrderRef,
                correlation.ProviderCancellationRef,
                CancellationObservationState.Unknown,
                null,
                null,
                TimeProvider.System.GetUtcNow(),
                CancellationResolutionSource.SupplierApi,
                CancellationReason.Uncorrelated
            );
        }

        public Task<ErrorOr.ErrorOr<CancellationEligibility>> InspectOrderAsync(
            string order,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr.ErrorOr<CancellationQuoteResult>> CreateTermsAsync(
            string order,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr.ErrorOr<CancellationEffectResult>> ConfirmAsync(
            CancellationTerms terms,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }
}
