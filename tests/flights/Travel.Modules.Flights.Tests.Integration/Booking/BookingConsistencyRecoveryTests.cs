using System.Collections.Concurrent;
using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

public sealed class BookingConsistencyRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operator_repairs_projection_and_replays_only_selected_DLQ_ids_without_repeating_commands(
        bool behindCorruption
    )
    {
        await using var pg = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await pg.StartAsync(Ct);
        var external = new ConvergenceExternalServices();
        var failure = new OperatorRecoveryFailure();
        using var hostA = await WolverineOutboxFixture.StartConvergenceHostAsync(
            pg.GetConnectionString(),
            new ConvergenceFaults(),
            external,
            true,
            Ct,
            services => services.AddSingleton(failure),
            options =>
                options.Policies.AddMiddleware<OperatorRecoveryMiddleware>(chain =>
                    chain.MessageType == typeof(ReconcileOrderReadModel)
                    || chain.MessageType == typeof(OrderConfirmedNotification)
                    || chain.MessageType == typeof(ProcessDuffelWebhookCommand)
                )
        );
        var owner = Guid.NewGuid();
        var id = await SeedHeldAsync(hostA, owner);
        var inboxId = Guid.NewGuid();
        try
        {
            await using (var scope = hostA.Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                var confirmed = await bus.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
                    new ConfirmOrderCommand(id, owner),
                    Ct
                );
                confirmed.IsError.ShouldBeFalse();
                var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
                db.WebhookInbox.Add(
                    new WebhookInboxEntity
                    {
                        Id = inboxId,
                        Source = "duffel",
                        EventId = "evt-recovery",
                        EventType = "order.created",
                        RawPayload =
                            "{\"object\":{\"id\":\"ord_"
                            + id
                            + "\",\"documents\":[{\"type\":\"ticket\",\"unique_identifier\":\"TKT-RECOVERY\"}]}}",
                        Signature = "test",
                        ReceivedAt = DateTimeOffset.UtcNow,
                    }
                );
                await db.SaveChangesAsync(Ct);
                await bus.PublishAsync(new ProcessDuffelWebhookCommand(inboxId));
                // An unselected allowed envelope must remain in DLQ throughout recovery.
                await bus.PublishAsync(new ReconcileOrderReadModel(Guid.NewGuid()));
            }
            var runtimeA = hostA.Services.GetRequiredService<IWolverineRuntime>();
            await WaitAsync(async () =>
                failure.Ids.Count == 4
                && (
                    await runtimeA.Storage.DeadLetters.QueryAsync(new() { PageSize = 100 }, Ct)
                ).TotalCount == 4
            );
            external.Confirmations.Count.ShouldBe(1);
            external.Authorizations.Count.ShouldBe(1);
            failure.Attempts.Values.ShouldAllBe(attempts => attempts == 4);
        }
        finally
        {
            await hostA.StopAsync(Ct);
        }

        Guid[] selected;
        Guid unselected;
        using (var maintenance = BuildMaintenanceHost(pg.GetConnectionString()))
        {
            await using var scope = maintenance.Services.CreateAsyncScope();
            var diagnostics =
                scope.ServiceProvider.GetRequiredService<IBookingConsistencyDiagnostics>();
            var runtime = maintenance.Services.GetRequiredService<IWolverineRuntime>();
            var letters = (
                await runtime.Storage.DeadLetters.QueryAsync(new() { PageSize = 100 }, Ct)
            ).Envelopes;
            var notification = letters
                .Single(x => x.MessageType == typeof(OrderConfirmedNotification).FullName)
                .Id;
            (await diagnostics.ReplayAsync(notification, Ct)).Code.ShouldBe(
                "ProjectionRepairRequired"
            );
            (await diagnostics.ReplayAsync(Guid.NewGuid(), Ct)).Code.ShouldBe("DeadLetterNotFound");
            (await diagnostics.ReplayAsync(Guid.Empty, Ct)).Code.ShouldBe("MessageIdRequired");
            var report = await scope
                .ServiceProvider.GetRequiredService<IOrderReadModelRebuildRunner>()
                .RunAsync(true, true, Ct);
            report.Failed.ShouldBe(0);
            var inspection = await diagnostics.InspectAsync(id, Ct);
            inspection.Validation.Issues.ShouldBeEmpty();
            inspection.Envelopes.Count.ShouldBe(3);
            selected = inspection.Envelopes.Select(x => x.MessageId).ToArray();
            unselected = letters.Single(x => !selected.Contains(x.Id)).Id;
            var maintenanceDb = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
            var corrupt = await EntityFrameworkQueryableExtensions.SingleAsync(
                maintenanceDb.Orders,
                x => x.AggregateId == id,
                Ct
            );
            corrupt.TotalAmount = 777;
            if (behindCorruption)
                corrupt.ProjectedStreamVersion = 3;
            await maintenanceDb.SaveChangesAsync(Ct);
            var reconcileId = inspection
                .Envelopes.Single(x => x.MessageType == typeof(ReconcileOrderReadModel).FullName)
                .MessageId;
            (await diagnostics.ReplayAsync(reconcileId, Ct)).Code.ShouldBe(
                "ProjectionRepairRequired"
            );
            await scope
                .ServiceProvider.GetRequiredService<IOrderReadModelRebuildRunner>()
                .RunAsync(true, true, Ct);
            var forbidden = new Envelope(new ConfirmOrderCommand(id, owner))
            {
                Id = Guid.NewGuid(),
                MessageType = typeof(ConfirmOrderCommand).FullName,
                Data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                    new ConfirmOrderCommand(id, owner)
                ),
                Destination = new Uri("local://default"),
            };
            await runtime.Storage.Inbox.MoveToDeadLetterStorageAsync(
                forbidden,
                new InvalidOperationException("test-only disallowed command")
            );
            (await diagnostics.ReplayAsync(forbidden.Id, Ct)).Code.ShouldBe(
                "MessageTypeNotAllowed"
            );
            (
                await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(forbidden.Id)
            )!.Replayable.ShouldBeFalse();
            foreach (var messageId in selected)
                (await diagnostics.ReplayAsync(messageId, Ct)).Replayable.ShouldBeTrue();
            (
                await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(unselected)
            )!.Replayable.ShouldBeFalse();
            var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
            (
                await EntityFrameworkQueryableExtensions.SingleAsync(
                    db.WebhookInbox.AsNoTracking(),
                    x => x.Id == inboxId,
                    Ct
                )
            ).ProcessedAt.ShouldBeNull();
        }

        using var hostB = await WolverineOutboxFixture.StartConvergenceHostAsync(
            pg.GetConnectionString(),
            new ConvergenceFaults(),
            external,
            true,
            Ct
        );
        try
        {
            await WaitAsync(async () =>
            {
                await using var scope = hostB.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
                var row = await EntityFrameworkQueryableExtensions.SingleAsync(
                    db.Orders.AsNoTracking(),
                    x => x.AggregateId == id,
                    Ct
                );
                var inbox = await EntityFrameworkQueryableExtensions.SingleAsync(
                    db.WebhookInbox.AsNoTracking(),
                    x => x.Id == inboxId,
                    Ct
                );
                return row.ProjectedStreamVersion == 5
                    && row.Status == "Ticketed"
                    && inbox.ProcessedAt is not null;
            });
            var runtime = hostB.Services.GetRequiredService<IWolverineRuntime>();
            await WaitAsync(async () =>
                !(await runtime.Storage.Admin.AllIncomingAsync()).Any(x =>
                    selected.Contains(x.Id) && x.Status != EnvelopeStatus.Handled
                )
            );
            foreach (var messageId in selected)
                (
                    await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(messageId)
                ).ShouldBeNull();
            (
                await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(unselected)
            ).ShouldNotBeNull();
            external.Confirmations.Count.ShouldBe(1);
            external.Authorizations.Count.ShouldBe(1);
            external.Captures.Count.ShouldBe(1);
            await using var session = hostB
                .Services.GetRequiredService<IDocumentStore>()
                .QuerySession();
            var events = await session.Events.FetchStreamAsync(id, token: Ct);
            events.Count(x => x.Data is OrderConfirmed).ShouldBe(1);
            events.Count(x => x.Data is OrderTicketed).ShouldBe(1);
        }
        finally
        {
            await hostB.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Pre_WS4_handler_surface_cannot_handle_the_new_reconcile_contract()
    {
        // A contract fixture, not certification of any historical deployable binary.
        using var legacy = new HostBuilder()
            .UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType<PreWs4NotificationSurface>();
            })
            .Build();
        await legacy.StartAsync(Ct);
        try
        {
            using var scope = legacy.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(
                new OrderConfirmedNotification(Guid.NewGuid(), Guid.NewGuid()),
                Ct
            );
            var failure = await Should.ThrowAsync<Exception>(() =>
                bus.InvokeAsync(new ReconcileOrderReadModel(Guid.NewGuid()), Ct)
            );
            failure.GetType().Name.ShouldBe("IndeterminateRoutesException");
            failure.Message.ShouldContain(nameof(ReconcileOrderReadModel));
        }
        finally
        {
            await legacy.StopAsync(Ct);
        }
    }

    internal static IHost BuildMaintenanceHost(string connection)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddBookingReadModelMaintenance(connection, true);
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(connection);
                options.AutoCreateSchemaObjects = AutoCreate.None;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine(x => x.AutoCreate = AutoCreate.None);
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            options.Durability.DurabilityAgentEnabled = false;
        });
        return builder.Build();
    }

    internal static async Task<Guid> SeedHeldAsync(IHost host, Guid owner)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        await using (
            var session = host.Services.GetRequiredService<IDocumentStore>().LightweightSession()
        )
        {
            session.Events.StartStream<BookingAggregate>(
                id,
                new OfferQuoted(
                    OfferId.New(),
                    Itinerary.Create([Slice.Create([segment]).Value]).Value,
                    BookingReconcilerFixture.Amount,
                    now.AddHours(1),
                    "off-test",
                    now
                ),
                BookingReconcilerFixture.Held(owner) with
                {
                    OrderId = "ord_" + id,
                    HeldUntil = now.AddHours(1),
                    HeldAt = now,
                }
            );
            await session.SaveChangesAsync(Ct);
        }
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IOrderReadModelReconciler>()
            .ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        return id;
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        while (!await condition())
            await Task.Delay(100, timeout.Token);
    }
}

public sealed class OperatorRecoveryFailure
{
    public ConcurrentDictionary<Guid, string> Ids { get; } = new();
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
}

public sealed class OperatorRecoveryMiddleware
{
    public static void Before(IMessageContext context, OperatorRecoveryFailure failure)
    {
        failure.Ids.TryAdd(context.Envelope!.Id, context.Envelope.MessageType!);
        failure.Attempts.AddOrUpdate(context.Envelope.Id, 1, (_, attempts) => attempts + 1);
        // Exercise the real 1s/5s/30s scoped retry policy through exhaustion.
        throw new BookingProjectionTransientException("ProjectionStorageUnavailable");
    }
}

public sealed class PreWs4NotificationSurface
{
    public static void Handle(OrderConfirmedNotification message) { }
}
