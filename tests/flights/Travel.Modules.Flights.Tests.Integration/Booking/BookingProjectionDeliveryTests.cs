using System.Collections.Concurrent;
using System.Threading.Channels;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class BookingProjectionDeliveryTests : IClassFixture<BookingDeliveryFixture>
{
    private readonly BookingDeliveryFixture _fixture;

    public BookingProjectionDeliveryTests(BookingDeliveryFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Webhook_write_conflict_retries_in_a_new_session_and_discards_losing_outbox()
    {
        var id = Guid.NewGuid();
        var scenario = _fixture.Probe.Add(id, failures: 0);
        scenario.InjectWriteConflict = true;
        await SeedProbeStreamAsync(id);
        await _fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(
                new ProcessDuffelWebhookCommand(id),
                new DeliveryOptions { CorrelationId = id.ToString() }
            );
        await scenario.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        scenario.Sessions.Count.ShouldBe(2);
        var sessions = scenario.Sessions.ToArray();
        sessions[0].ShouldNotBeSameAs(sessions[1]);
        scenario.Attempts.ShouldBeEmpty();
        _fixture.Probe.SideEffects.GetValueOrDefault(id).ShouldBe(0);
        var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        (await runtime.Storage.Admin.AllOutgoingAsync()).ShouldNotContain(x =>
            x.CorrelationId == id.ToString()
        );
        (await runtime.Storage.Admin.AllIncomingAsync()).ShouldNotContain(x =>
            x.CorrelationId == id.ToString()
            && x.MessageType != typeof(ProcessDuffelWebhookCommand).FullName
        );
        await using var verify = _fixture
            .Host.Services.GetRequiredService<IDocumentStore>()
            .QuerySession();
        (await verify.Events.FetchStreamAsync(id, token: Ct)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Raw_storage_failure_after_webhook_commit_retries_ack_without_republishing()
    {
        var id = Guid.NewGuid();
        var scenario = _fixture.Probe.Add(id, failures: 0);
        scenario.InjectPostCommitFailure = true;
        await SeedProbeStreamAsync(id);
        await _fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new ProcessDuffelWebhookCommand(id));
        await scenario.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        try
        {
            await scenario.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        }
        catch (TimeoutException)
        {
            var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
            var incoming = await runtime.Storage.Admin.AllIncomingAsync();
            var outgoing = await runtime.Storage.Admin.AllOutgoingAsync();
            TestContext.Current.TestOutputHelper?.WriteLine(
                "Incoming: "
                    + string.Join(
                        "; ",
                        incoming.Select(x => $"{x.MessageType} {x.Id} {x.Status} owner={x.OwnerId}")
                    )
            );
            TestContext.Current.TestOutputHelper?.WriteLine(
                "Outgoing: "
                    + string.Join(
                        "; ",
                        outgoing.Select(x => $"{x.MessageType} {x.Id} owner={x.OwnerId}")
                    )
            );
            throw;
        }
        await WaitForAsync(() =>
            Task.FromResult(_fixture.Probe.SideEffects.GetValueOrDefault(id) == 1)
        );
        scenario.Sessions.Count.ShouldBe(2);
        scenario.Attempts.ShouldHaveSingleItem();
        await using var verify = _fixture
            .Host.Services.GetRequiredService<IDocumentStore>()
            .QuerySession();
        (await verify.Events.FetchStreamAsync(id, token: Ct)).Count.ShouldBe(2);
    }

    private async Task SeedProbeStreamAsync(Guid id)
    {
        await using var session = _fixture
            .Host.Services.GetRequiredService<IDocumentStore>()
            .LightweightSession();
        session.Events.StartStream(id, new DeliveryConflictEvent(1));
        await session.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Production_reconciler_is_resolvable_and_runs_through_the_durable_handler()
    {
        await _fixture.RestartAsync(agentEnabled: true, realReconciler: true);
        try
        {
            var id = Guid.NewGuid();
            var owner = Guid.NewGuid();
            var at = BookingReconcilerFixture.Now;
            var segment = Segment
                .Create(
                    IataCode.Create("LED").Value,
                    IataCode.Create("DME").Value,
                    at.AddDays(1),
                    at.AddDays(1).AddHours(2),
                    "SU",
                    "100",
                    CabinClass.Economy
                )
                .Value;
            var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
            await using (var session = store.LightweightSession())
            {
                session.Events.StartStream<BookingAggregate>(
                    id,
                    new OfferQuoted(
                        OfferId.New(),
                        Itinerary.Create([Slice.Create([segment]).Value]).Value,
                        BookingReconcilerFixture.Amount,
                        at.AddHours(1),
                        "off-test",
                        at
                    ),
                    BookingReconcilerFixture.Held(owner)
                );
                await session.SaveChangesAsync(Ct);
            }
            var bus = _fixture.Host.Services.GetRequiredService<IMessageBus>();
            // Inline call exposes DI/codegen failures directly; the following publish proves the same handler's queue path.
            await bus.InvokeAsync(new ReconcileOrderReadModel(id), Ct);
            await using (var session = store.LightweightSession())
            {
                session.Events.Append(
                    id,
                    new PaymentAuthorized(PaymentRef.New(), BookingReconcilerFixture.Amount, at)
                );
                await session.SaveChangesAsync(Ct);
            }
            await bus.PublishAsync(new ReconcileOrderReadModel(id));
            await WaitForAsync(async () =>
            {
                await using var scope = _fixture.Host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
                return await EntityFrameworkQueryableExtensions.AnyAsync(
                    db.Orders,
                    x => x.AggregateId == id && x.ProjectedStreamVersion == 3,
                    Ct
                );
            });
            await using (var session = store.LightweightSession())
            {
                session.Events.Append(id, new OrderConfirmed("ord-test", PaymentRef.New(), at));
                await session.SaveChangesAsync(Ct);
            }
            await bus.InvokeAsync(new ReconcileOrderReadModel(id), Ct);
            var registry = _fixture.Host.Services.GetRequiredService<IOrderSseRegistry>();
            var channel = Channel.CreateUnbounded<SseEvent>();
            registry.Register(id, channel);
            try
            {
                await bus.InvokeAsync(new OrderConfirmedNotification(id, owner, 4), Ct);
                channel.Reader.TryRead(out var evt).ShouldBeTrue();
                evt!.StreamVersion.ShouldBe(4);
                evt.Type.ShouldBe("OrderConfirmed");
                _fixture
                    .Host.Services.GetRequiredService<NotificationTransportProbe>()
                    .Sent.ShouldBe(1);
            }
            finally
            {
                registry.Unregister(id, channel);
            }
        }
        finally
        {
            await _fixture.RestartAsync(agentEnabled: true);
        }
    }

    [Fact]
    public async Task Raw_transient_storage_error_retries_with_a_new_scope_and_eventually_succeeds()
    {
        var id = Guid.NewGuid();
        var scenario = _fixture.Probe.Add(id, failures: 1);
        await _fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new ReconcileOrderReadModel(id));
        await scenario.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        scenario.Attempts.Count.ShouldBe(2);
        scenario.Attempts.Select(x => x.ScopeId).Distinct().Count().ShouldBe(2);
        await WaitForAsync(() => Task.FromResult(scenario.Disposed.Count == 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_and_unknown_errors_are_persisted_in_DLQ_without_retry(bool unknown)
    {
        var id = Guid.NewGuid();
        var scenario = _fixture.Probe.Add(
            id,
            failures: int.MaxValue,
            terminal: true,
            unknown: unknown
        );
        await _fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new ReconcileOrderReadModel(id));
        await WaitForDeadLetterAsync(scenario);
        scenario.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_exhaustion_moves_the_message_to_DLQ_after_four_attempts()
    {
        var id = Guid.NewGuid();
        var scenario = _fixture.Probe.Add(id, failures: int.MaxValue);
        await _fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new ReconcileOrderReadModel(id));
        await WaitForDeadLetterAsync(scenario);
        scenario.Attempts.Count.ShouldBe(4);
        scenario.Attempts.Select(x => x.ScopeId).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task Persisted_pending_message_is_recovered_by_a_new_host_without_republishing()
    {
        await _fixture.RestartAsync(agentEnabled: false);
        var id = Guid.NewGuid();
        var scenario = _fixture.Probe.Add(id, failures: 0);
        await _fixture
            .Host.Services.GetRequiredService<IMessageBus>()
            .ScheduleAsync(
                new ReconcileOrderReadModel(id),
                TimeSpan.FromSeconds(2),
                new DeliveryOptions { CorrelationId = id.ToString() }
            );
        var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        var persisted = (await runtime.Storage.Admin.AllIncomingAsync()).Single(x =>
            x.CorrelationId == id.ToString()
        );
        scenario.Attempts.ShouldBeEmpty();
        await _fixture.RestartAsync(agentEnabled: true);
        await scenario.Succeeded.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        scenario.Attempts.ShouldHaveSingleItem().EnvelopeId.ShouldBe(persisted.Id);
    }

    private Task WaitForDeadLetterAsync(DeliveryScenario scenario) =>
        WaitForAsync(async () =>
        {
            var attempt = scenario.Attempts.FirstOrDefault();
            if (attempt is null)
                return false;
            var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
            return await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(attempt.EnvelopeId)
                is not null;
        });

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(50));
        while (!await condition())
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
    }
}

public sealed record DeliveryAttempt(Guid ScopeId, Guid EnvelopeId);

public sealed class DeliveryScenario(int failures, bool terminal, bool unknown)
{
    public bool InjectWriteConflict { get; set; }
    public bool InjectPostCommitFailure { get; set; }
    public ConcurrentQueue<IDocumentSession> Sessions { get; } = new();
    public TaskCompletionSource Acknowledged { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<DeliveryAttempt> Attempts { get; } = new();
    public ConcurrentQueue<Guid> Disposed { get; } = new();
    public TaskCompletionSource Succeeded { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Run(Guid scopeId, Guid envelopeId)
    {
        Attempts.Enqueue(new(scopeId, envelopeId));
        if (Attempts.Count <= failures)
        {
            if (unknown)
                throw new InvalidOperationException("Injected unexpected failure");
            if (terminal)
                throw new BookingProjectionTerminalException("SourceOwnerMissing");
            throw new DbUpdateException(
                "Injected storage fault",
                new PostgresException("deadlock", "ERROR", "ERROR", "40P01")
            );
        }
        Succeeded.TrySetResult();
    }
}

public sealed class BookingDeliveryProbe
{
    public ConcurrentDictionary<Guid, Guid> EnvelopeIds { get; } = new();
    public ConcurrentDictionary<Guid, int> SideEffects { get; } = new();
    private readonly ConcurrentDictionary<Guid, DeliveryScenario> _scenarios = new();

    public DeliveryScenario Add(
        Guid id,
        int failures,
        bool terminal = false,
        bool unknown = false
    ) => _scenarios[id] = new(failures, terminal, unknown);

    public DeliveryScenario Get(Guid id) => _scenarios[id];
}

public sealed class DeliveryReconciler(BookingDeliveryProbe probe)
    : IOrderReadModelReconciler,
        IDisposable
{
    private readonly Guid _scopeId = Guid.NewGuid();
    private DeliveryScenario? _scenario;

    public Task<ReconcileResult> ReconcileAsync(
        Guid aggregateId,
        OrderReadModelReconcileMode mode,
        CancellationToken ct
    )
    {
        mode.ShouldBe(OrderReadModelReconcileMode.Incremental);
        _scenario = probe.Get(aggregateId);
        _scenario.Run(_scopeId, probe.EnvelopeIds[aggregateId]);
        return Task.FromResult(new ReconcileResult(aggregateId, null, 2, 2, 2, true));
    }

    public Task<ProjectionValidation> ValidateAsync(Guid aggregateId, CancellationToken ct) =>
        throw new NotSupportedException();

    public void Dispose() => _scenario?.Disposed.Enqueue(_scopeId);
}

public sealed class BookingDeliveryFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    public BookingDeliveryProbe Probe { get; } = new();
    public IHost Host { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await StartAsync(true);
    }

    private async Task StartAsync(bool agentEnabled, bool realReconciler = false)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(Probe);
        builder.Services.AddDbContext<FlightsDbContext>(options =>
            options
                .UseNpgsql(
                    _pg.GetConnectionString(),
                    x => x.MigrationsHistoryTable("__ef_migrations_history", "flights")
                )
                .UseSnakeCaseNamingConvention()
        );
        if (realReconciler)
        {
            builder.Services.AddScoped<IOrderReadModelReconciler, OrderReadModelReconciler>();
            builder.Services.AddScoped<
                IBookingNotificationReadiness,
                BookingNotificationReadiness
            >();
            builder.Services.AddSingleton<IOrderSseRegistry, OrderSseConnectionRegistry>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<NotificationTransportProbe>();
            builder.Services.AddSingleton<IEmailSender>(sp =>
                sp.GetRequiredService<NotificationTransportProbe>()
            );
            builder.Services.AddSingleton<IEmailRenderer>(sp =>
                sp.GetRequiredService<NotificationTransportProbe>()
            );
            builder.Services.AddSingleton<IUserDirectory>(sp =>
                sp.GetRequiredService<NotificationTransportProbe>()
            );
            builder.Services.AddSingleton<
                IBookingProjectionMaintenanceContext,
                BookingProjectionMaintenanceContext
            >();
        }
        else
            builder.Services.AddScoped<IOrderReadModelReconciler, DeliveryReconciler>();
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(_pg.GetConnectionString());
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ReconcileOrderReadModelHandler).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(ReconcileOrderReadModelHandler));
            if (realReconciler)
            {
                options.Discovery.IncludeType(typeof(PublishOrderSseHandler));
                options.Discovery.IncludeType(typeof(SendOrderConfirmationEmailHandler));
                options.Discovery.IncludeType(typeof(SendOrderCancellationEmailHandler));
            }
            options.Discovery.IncludeType(typeof(WebhookDeliveryProbe));
            options.Discovery.IncludeType(typeof(DeliverySideEffectProbe));
            options.Policies.UseDurableLocalQueues();
            options.Policies.AddMiddleware<DeliveryCaptureMiddleware>(chain =>
                chain.MessageType == typeof(ReconcileOrderReadModel)
            );
            BookingConsistencyHandlerPolicy.Configure(options);
            options.Services.RunWolverineInSoloMode();
            options.Durability.DurabilityAgentEnabled = agentEnabled;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
        });
        Host = builder.Build();
        await using (var scope = Host.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<FlightsDbContext>()
                .Database.MigrateAsync();
        await Host.StartAsync();
    }

    public async Task RestartAsync(bool agentEnabled, bool realReconciler = false)
    {
        await Host.StopAsync();
        Host.Dispose();
        await StartAsync(agentEnabled, realReconciler);
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }
        await _pg.DisposeAsync();
    }
}

public sealed record DeliveryConflictEvent(int Value);

public sealed class DeliveryCaptureMiddleware
{
    public static void Before(
        ReconcileOrderReadModel message,
        IMessageContext context,
        BookingDeliveryProbe probe
    ) => probe.EnvelopeIds[message.AggregateId] = context.Envelope!.Id;
}

public sealed record DeliverySideEffect(Guid AggregateId);

public static class DeliverySideEffectProbe
{
    public static void Handle(DeliverySideEffect message, BookingDeliveryProbe probe) =>
        probe.SideEffects.AddOrUpdate(message.AggregateId, 1, (_, count) => count + 1);
}

// Test-only orchestration probe: same real Marten commit helper and actual webhook message policy.
// Business transition decisions remain covered by BookingWebhookConcurrencyTests.
public static class WebhookDeliveryProbe
{
    public static async Task Handle(
        ProcessDuffelWebhookCommand message,
        IDocumentStore store,
        IDocumentSession session,
        IMartenOutbox outbox,
        BookingDeliveryProbe probe,
        CancellationToken ct
    )
    {
        var scenario = probe.Get(message.InboxId);
        scenario.Sessions.Enqueue(session);
        var state = await session.Events.FetchStreamStateAsync(message.InboxId, ct);
        if (state!.Version == 1)
        {
            session.Events.Append(message.InboxId, state.Version + 1, new DeliveryConflictEvent(2));
            if (scenario.InjectWriteConflict)
            {
                await using var winner = store.LightweightSession();
                winner.Events.Append(message.InboxId, new DeliveryConflictEvent(99));
                await winner.SaveChangesAsync(ct);
            }
            await session.SaveBookingWithReconcileAsync(
                outbox,
                message.InboxId,
                [new DeliverySideEffect(message.InboxId)],
                ct
            );
            if (scenario.InjectPostCommitFailure)
                throw new DbUpdateException(
                    "Injected inbox acknowledgement failure",
                    new PostgresException("deadlock", "ERROR", "ERROR", "40P01")
                );
        }
        scenario.Acknowledged.TrySetResult();
    }
}

public sealed class NotificationTransportProbe : IEmailSender, IEmailRenderer, IUserDirectory
{
    public int Sent { get; private set; }

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct
    )
    {
        Sent++;
        return Task.CompletedTask;
    }

    public Task<RenderedEmail> RenderAsync(
        string templateName,
        OrderEmailModel model,
        System.Globalization.CultureInfo locale,
        CancellationToken ct
    ) => Task.FromResult(new RenderedEmail("test", "html", "text"));

    public Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<UserProfile?>(
            new UserProfile(userId, "test@example.test", "Test", "User", "en")
        );
}
