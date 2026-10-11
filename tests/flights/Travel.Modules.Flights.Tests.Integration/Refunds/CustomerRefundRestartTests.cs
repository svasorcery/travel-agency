using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Refunds;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Infrastructure.Cancellation;
using Travel.Modules.Flights.Tests.Integration.Cancellation;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Refunds;

// Compile locally only. Existing CI executes disposable store/schema-backed runtime.
[Trait("Category", "Integration")]
public sealed class CustomerRefundRestartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lost_result_reaches_retained_deadline_after_host_replacement_without_owner_GET()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var count = new Counter();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var cancellation = Guid.NewGuid();
        var refund = Guid.NewGuid();
        DateTimeOffset deadline;
        // Historical admission is 170s old. Production duration remains exactly 180s.
        var admitted = TimeProvider.System.GetUtcNow().AddSeconds(-170);
        using (var a = await Start(postgres.GetConnectionString(), count, false))
        {
            await Seed(a, id, owner, cancellation, admitted);
            await Admit(a, id, owner, cancellation, refund, admitted);
            var unknown = await Wait(
                a,
                id,
                b => b.CustomerRefund?.State == CustomerRefundState.Unknown
            );
            deadline = unknown.CustomerRefund!.DeadlineAt;
            deadline.ShouldBe(admitted.AddSeconds(180));
            count.Calls.ShouldBe(1);
            await a.StopAsync(Ct);
        }
        using (var b = await Start(postgres.GetConnectionString(), count, false))
        {
            var manual = await Wait(
                b,
                id,
                x => x.CustomerRefund?.State == CustomerRefundState.ManualReviewRequired
            );
            manual.CustomerRefund!.Id.ShouldBe(refund);
            manual.CustomerRefund.DeadlineAt.ShouldBe(deadline);
            manual.CustomerRefund.Basis!.CapturedAmount.Amount.ShouldBe(100m);
            using var replayScope = b.Services.CreateScope();
            var replayBus = replayScope.ServiceProvider.GetRequiredService<IMessageBus>();
            await replayBus.InvokeAsync(new ExecuteCustomerRefund(id, refund), Ct);
            await replayBus.InvokeAsync(new ExecuteCustomerRefund(id, refund), Ct);
            count.Calls.ShouldBe(1);
            manual.CustomerRefund.Observation.ShouldBeNull();
            await WaitNotification(id, manual.Version);
            await b.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task External_sent_receipt_with_failed_local_save_never_sends_again_and_stays_manual()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var count = new Counter();
        var failure = new FailOnce(false);
        using var host = await Start(postgres.GetConnectionString(), count, true, failure);
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var cancellation = Guid.NewGuid();
        var refund = Guid.NewGuid();
        var admitted = TimeProvider.System.GetUtcNow().AddSeconds(-170);
        await Seed(host, id, owner, cancellation, admitted);
        await Admit(host, id, owner, cancellation, refund, admitted);
        var manual = await Wait(
            host,
            id,
            b => b.CustomerRefund?.State == CustomerRefundState.ManualReviewRequired
        );
        count.Calls.ShouldBe(1);
        failure.Failures.ShouldBe(1);
        manual.CustomerRefund!.Observation.ShouldBeNull();
        await using var read = host
            .Services.GetRequiredService<IDocumentStore>()
            .LightweightSession();
        var events = await read.Events.FetchStreamAsync(id, token: Ct);
        events
            .Any(e =>
                e.Data
                    is CustomerRefundOutcomeRecorded { Observation.State: CustomerRefundState.Sent }
            )
            .ShouldBeFalse();
        using var replayScope = host.Services.CreateScope();
        await replayScope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new ExecuteCustomerRefund(id, refund), Ct);
        count.Calls.ShouldBe(1);
        await host.StopAsync(Ct);
    }

    [Fact]
    public async Task Admission_rollback_publishes_no_work_and_retry_commits_one_case()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var count = new Counter();
        var failure = new FailOnce(true);
        using var host = await Start(postgres.GetConnectionString(), count, true, failure);
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var cancellation = Guid.NewGuid();
        var admitted = TimeProvider.System.GetUtcNow();
        await Seed(host, id, owner, cancellation, admitted);
        await Should.ThrowAsync<IOException>(() =>
            Admit(host, id, owner, cancellation, Guid.NewGuid(), admitted)
        );
        await Task.Delay(300, Ct);
        await using (
            var read = host.Services.GetRequiredService<IDocumentStore>().LightweightSession()
        )
            (
                await read.Events.FetchForWriting<BookingAggregate>(id, Ct)
            ).Aggregate!.CustomerRefund.ShouldBeNull();
        count.Calls.ShouldBe(0);
        OrderChangedStorageProbeHandler.Versions(id).ShouldBeEmpty();
        var refund = Guid.NewGuid();
        await Admit(host, id, owner, cancellation, refund, admitted);
        var sent = await Wait(host, id, b => b.CustomerRefund?.State == CustomerRefundState.Sent);
        sent.CustomerRefund!.Id.ShouldBe(refund);
        failure.Failures.ShouldBe(1);
        count.Calls.ShouldBe(1);
        await WaitNotification(id, sent.Version);
        await host.StopAsync(Ct);
    }

    private static async Task<IHost> Start(
        string connection,
        Counter count,
        bool sent,
        FailOnce? failure = null
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDispatchInstanceIdentity, ProcessDispatchInstanceIdentity>();
        builder.Services.AddSingleton(new CustomerRefundAvailability(true));
        builder.Services.AddSingleton<ICustomerRefundGateway>(new Gateway(count, sent));
        builder
            .Services.AddMarten(o =>
            {
                o.Connection(connection);
                o.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(o);
                if (failure is not null)
                    o.Listeners.Add(failure);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(o =>
        {
            o.Discovery.DisableConventionalDiscovery();
            foreach (
                var type in new[]
                {
                    typeof(AssessCustomerRefundHandler),
                    typeof(ExecuteCustomerRefundHandler),
                    typeof(CustomerRefundDeadlineHandler),
                    typeof(CancellationReconcileProbeHandler),
                    typeof(OrderChangedStorageProbeHandler),
                }
            )
                o.Discovery.IncludeType(type);
            o.Policies.AutoApplyTransactions();
            o.Policies.UseDurableLocalQueues();
            CustomerRefundDeliveryPolicy.Configure(o);
            o.Services.RunWolverineInSoloMode();
            o.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
        });
        var host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }

    private static async Task Seed(
        IHost host,
        Guid id,
        Guid owner,
        Guid cancellation,
        DateTimeOffset now
    )
    {
        await using var s = host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        s.Events.StartStream<BookingAggregate>(
            id,
            CustomerRefundPersistenceTests
                .ProvenCancellation(id, owner, cancellation, now)
                .Cast<object>()
                .ToArray()
        );
        await s.SaveChangesAsync(Ct);
    }

    private static async Task Admit(
        IHost host,
        Guid id,
        Guid owner,
        Guid cancellation,
        Guid refund,
        DateTimeOffset now
    )
    {
        using var scope = host.Services.CreateScope();
        var s = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var stream = await s.Events.FetchForWriting<BookingAggregate>(id, Ct);
        var d = stream.Aggregate!.DecideCustomerRefundAdmission(owner, cancellation, refund, now);
        d.IsError.ShouldBeFalse();
        await CustomerRefundWriter.Persist(
            d.Value,
            stream,
            s,
            scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            Ct
        );
    }

    private static async Task<BookingAggregate> Wait(
        IHost host,
        Guid id,
        Func<BookingAggregate, bool> predicate
    )
    {
        var until = TimeProvider.System.GetUtcNow().AddSeconds(45);
        while (TimeProvider.System.GetUtcNow() < until)
        {
            await using var s = host
                .Services.GetRequiredService<IDocumentStore>()
                .LightweightSession();
            var b = (await s.Events.FetchForWriting<BookingAggregate>(id, Ct)).Aggregate;
            if (b is not null && predicate(b))
                return b;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("Fictional refund did not reach expected retained state.");
    }

    private static async Task WaitNotification(Guid id, long version)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!OrderChangedStorageProbeHandler.Versions(id).Contains(version))
            await Task.Delay(50, timeout.Token);
    }

    private sealed class Counter
    {
        public int Calls;
    }

    [TestOnly]
    private sealed class Gateway(Counter count, bool sent) : ICustomerRefundGateway
    {
        public Task<ErrorOr<CustomerRefundObservation>> ExecuteAsync(
            CustomerRefundRequest r,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref count.Calls);
            if (!sent)
                return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
                    Error.Failure("Fictional.Lost", "Fictional result lost.")
                );
            return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
                new CustomerRefundObservation(
                    r.RefundId,
                    r.OriginalPayment.PaymentReference,
                    r.Amount,
                    r.Destination,
                    CustomerRefundState.Sent,
                    "fref_runtime",
                    TimeProvider.System.GetUtcNow(),
                    CustomerRefundEvidenceSource.FictionalGateway,
                    false
                )
            );
        }
    }

    private sealed class FailOnce(bool admission) : DocumentSessionListenerBase
    {
        public int Failures;

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken ct)
        {
            if (
                session
                    .PendingChanges.Streams()
                    .Any(s =>
                        s.Events.Any(e =>
                            admission
                                ? e.Data is CustomerRefundRequested
                                : e.Data is CustomerRefundOutcomeRecorded
                        )
                    )
                && Interlocked.CompareExchange(ref Failures, 1, 0) == 0
            )
                throw new IOException("Synthetic refund commit failure before save.");
            return Task.CompletedTask;
        }
    }
}
