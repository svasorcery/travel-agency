using System.Text.Json;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

[Trait("Category", "Integration")]
public sealed class CancellationDeliveryTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task Prepare_admission_saves_owner_operation_before_any_supplier_mutation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using var scope = fixture.Host.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        session.Events.StartStream<BookingAggregate>(
            id,
            CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
        );
        await session.SaveChangesAsync(ct);
        var stream = await session.Events.FetchForWriting<BookingAggregate>(id, ct);
        var op = Guid.NewGuid();
        var result = await PrepareCancellationHandler.Handle(
            new(id, owner, op, stream.CurrentVersion!.Value, new string('a', 64)),
            session,
            scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            TimeProvider.System,
            ct
        );
        result.IsError.ShouldBeFalse();
        result.Value.Operation!.Phase.ShouldBe("Preparing");
        using var verify = fixture.Host.Services.CreateScope();
        var loaded = await verify
            .ServiceProvider.GetRequiredService<IDocumentSession>()
            .Events.FetchForWriting<BookingAggregate>(id, ct);
        loaded.Aggregate!.CurrentCancellationId.ShouldBe(op);
        loaded.Aggregate.CurrentCancellation!.OwnerId.ShouldBe(owner);
        loaded.Aggregate.CurrentCancellation.PreparationDispatchedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Redelivered_saved_create_claim_never_calls_supplier_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using var scope = fixture.Host.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        session.Events.StartStream<BookingAggregate>(
            id,
            CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
        );
        await session.SaveChangesAsync(ct);
        var stream = await session.Events.FetchForWriting<BookingAggregate>(id, ct);
        var op = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var started = stream.Aggregate!.DecideCancellationPrepare(
            owner,
            op,
            stream.CurrentVersion!.Value,
            new string('a', 64),
            admission,
            now,
            new string('b', 64)
        );
        foreach (var e in started.Events)
            stream.AppendOne(e);
        await session.SaveChangesAsync(ct);
        stream = await session.Events.FetchForWriting<BookingAggregate>(id, ct);
        var claim = stream.Aggregate!.DecideCancellationPreparationDispatch(
            op,
            stream.Aggregate.CurrentCancellation!.Revision,
            admission,
            Guid.NewGuid(),
            Guid.NewGuid(),
            now
        );
        foreach (var e in claim.Events)
            stream.AppendOne(e);
        await session.SaveChangesAsync(ct);
        using var fresh = fixture.Host.Services.CreateScope();
        var supplier = new ForbiddenSupplier();
        await ExecuteCancellationPreparationHandler.Handle(
            new(id, op, admission),
            fresh.ServiceProvider.GetRequiredService<IDocumentSession>(),
            fresh.ServiceProvider.GetRequiredService<IDocumentStore>(),
            fresh.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            supplier,
            new Identity(),
            TimeProvider.System,
            ct
        );
        supplier.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Redelivered_confirmation_claim_never_inspects_or_confirms_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var op = Guid.NewGuid();
        var prepare = Guid.NewGuid();
        var confirm = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using (var seed = fixture.Host.Services.CreateScope())
        {
            var session = seed.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
            );
            await session.SaveChangesAsync(ct);
            var loaded = await session.Events.FetchForWriting<BookingAggregate>(id, ct);
            var hash = CancellationScope.Create(loaded.Aggregate!).Value;
            var terms = CancellationTerms
                .Create(
                    new(
                        1,
                        id,
                        owner,
                        op,
                        "ord_fictional",
                        "occ_fictional",
                        hash,
                        Money.Create(17.25m, CurrencyCode.Create("USD").Value).Value,
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
            session.Events.Append(
                id,
                new CancellationPreparationStarted(
                    op,
                    owner,
                    "ord_fictional",
                    hash,
                    new string('a', 64),
                    prepare,
                    now
                ),
                new CancellationPreparationDispatched(
                    op,
                    prepare,
                    Guid.NewGuid(),
                    RecoverySchedule.Create(Guid.NewGuid(), now).Value,
                    now
                ),
                new CancellationTermsObtained(op, terms, now),
                new CancellationTermsAccepted(
                    op,
                    terms.Revision,
                    terms.Hash,
                    terms.NoticeVersion,
                    new string('b', 64),
                    confirm,
                    now
                ),
                new CancellationConfirmationDispatched(
                    op,
                    confirm,
                    Guid.NewGuid(),
                    RecoverySchedule.Create(Guid.NewGuid(), now).Value,
                    now
                )
            );
            await session.SaveChangesAsync(ct);
        }
        using var fresh = fixture.Host.Services.CreateScope();
        var supplier = new ForbiddenSupplier();
        await ExecuteCancellationConfirmationHandler.Handle(
            new(id, op, confirm),
            fresh.ServiceProvider.GetRequiredService<IDocumentSession>(),
            fresh.ServiceProvider.GetRequiredService<IDocumentStore>(),
            fresh.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            supplier,
            new Identity(),
            TimeProvider.System,
            ct
        );
        supplier.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Winning_create_claim_and_five_recovery_envelopes_exist_before_supplier_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using (var seed = fixture.Host.Services.CreateScope())
        {
            var session = seed.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
            );
            await session.SaveChangesAsync(ct);
            var loaded = await session.Events.FetchForWriting<BookingAggregate>(id, ct);
            var scopeHash = CancellationScope.Create(loaded.Aggregate!).Value;
            loaded.AppendOne(
                new CancellationPreparationStarted(
                    operation,
                    owner,
                    "ord_fictional",
                    scopeHash,
                    new string('a', 64),
                    admission,
                    now
                )
            );
            await session.SaveChangesAsync(ct);
        }
        using var scope = fixture.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var supplier = new ClaimBeforeSupplierProbe(
            store,
            fixture.Host.Services.GetRequiredService<IWolverineRuntime>(),
            id,
            operation
        );
        await ExecuteCancellationPreparationHandler.Handle(
            new(id, operation, admission),
            scope.ServiceProvider.GetRequiredService<IDocumentSession>(),
            store,
            scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            supplier,
            new Identity(),
            TimeProvider.System,
            ct
        );
        supplier.Calls.ShouldBe(1);
        await using var verify = store.LightweightSession();
        var current = await verify.Events.FetchForWriting<BookingAggregate>(id, ct);
        current.Aggregate!.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Unknown);
    }

    private sealed class ClaimBeforeSupplierProbe(
        IDocumentStore store,
        IWolverineRuntime runtime,
        Guid id,
        Guid operation
    ) : IFlightCancellationProvider
    {
        public int Calls { get; private set; }

        public async Task<ErrorOr.ErrorOr<CancellationQuoteResult>> CreateTermsAsync(
            string order,
            CancellationToken ct
        )
        {
            Calls++;
            await using var verify = store.LightweightSession();
            var stream = await verify.Events.FetchForWriting<BookingAggregate>(id, ct);
            var claim = stream.Aggregate!.CancellationOperations[operation];
            claim.PreparationDispatchedAt.ShouldNotBeNull();
            claim.Recovery.ShouldNotBeNull();
            var envelopes = (await runtime.Storage.Admin.AllIncomingAsync()).Concat(
                await runtime.Storage.Admin.AllOutgoingAsync()
            );
            var jobs = new HashSet<Guid>();
            foreach (
                var envelope in envelopes.Where(e =>
                    e.Data is not null
                    && (
                        e.MessageType == typeof(ObserveCancellation).FullName
                        || e.MessageType == typeof(RecoveryDeadline).FullName
                    )
                )
            )
            {
                using var data = JsonDocument.Parse(envelope.Data!);
                var fields = data
                    .RootElement.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                if (fields.TryGetValue("OperationId", out var op) && op.GetGuid() == operation)
                    jobs.Add(envelope.Id);
            }
            jobs.Count.ShouldBe(5);
            return new CancellationQuoteResult(
                CancellationQuoteOutcome.Unknown,
                null,
                CancellationReason.ProviderUnavailable
            );
        }

        public Task<ErrorOr.ErrorOr<CancellationEligibility>> InspectOrderAsync(
            string order,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr.ErrorOr<CancellationEffectResult>> ConfirmAsync(
            CancellationTerms terms,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr.ErrorOr<CancellationObservation>> ObserveAsync(
            CancellationCorrelation correlation,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class Identity : IDispatchInstanceIdentity
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ForbiddenSupplier : IFlightCancellationProvider
    {
        public int Calls { get; private set; }

        public Task<ErrorOr.ErrorOr<CancellationEligibility>> InspectOrderAsync(
            string order,
            CancellationToken ct
        )
        {
            Calls++;
            throw new InvalidOperationException("Unexpected synthetic supplier call.");
        }

        public Task<ErrorOr.ErrorOr<CancellationQuoteResult>> CreateTermsAsync(
            string order,
            CancellationToken ct
        )
        {
            Calls++;
            throw new InvalidOperationException("Unexpected synthetic supplier call.");
        }

        public Task<ErrorOr.ErrorOr<CancellationEffectResult>> ConfirmAsync(
            CancellationTerms terms,
            CancellationToken ct
        )
        {
            Calls++;
            throw new InvalidOperationException("Unexpected synthetic supplier call.");
        }

        public Task<ErrorOr.ErrorOr<CancellationObservation>> ObserveAsync(
            CancellationCorrelation correlation,
            CancellationToken ct
        )
        {
            Calls++;
            throw new InvalidOperationException("Unexpected synthetic supplier call.");
        }
    }
}
