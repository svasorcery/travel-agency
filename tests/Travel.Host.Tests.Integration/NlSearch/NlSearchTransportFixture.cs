extern alias TravelAiApp;
extern alias TravelHostApp;
using Alba;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ErrorOr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Testcontainers.PostgreSql;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine;

namespace Travel.Host.Tests.Integration.NlSearch;

public sealed class NlSearchTransportFixture : IAsyncDisposable
{
    private const int NatsPort = 4222;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(1);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("travel_transport_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly IContainer _nats = new ContainerBuilder("library/nats:2.12")
        .WithPortBinding(NatsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .Build();

    private readonly TransportFixtureLifecycle _lifecycle = new(CleanupTimeout);

    private NatsConnection? _observer;
    private INatsSub<byte[]>? _observedMessages;
    private IAlbaHost? _aiHost;
    private IAlbaHost? _host;

    private NlSearchTransportFixture()
    {
        _lifecycle.RegisterCleanup("PostgreSQL", _ => _postgres.DisposeAsync());
        _lifecycle.RegisterCleanup("Core NATS", _ => _nats.DisposeAsync());
    }

    public static async Task<NlSearchTransportFixture> StartAsync(CancellationToken ct)
    {
        var fixture = new NlSearchTransportFixture();
        try
        {
            await fixture._lifecycle.RunCancellablePhaseAsync(
                "containers",
                phaseCt =>
                    Task.WhenAll(
                        StartContainerAsync(fixture._postgres, "PostgreSQL", phaseCt),
                        StartContainerAsync(fixture._nats, "Core NATS", phaseCt)
                    ),
                TimeSpan.FromSeconds(30),
                ct
            );

            var natsUrl =
                $"nats://{fixture._nats.Hostname}:{fixture._nats.GetMappedPublicPort(NatsPort)}";
            fixture._observer = new NatsConnection(new NatsOpts { Url = natsUrl });
            fixture._lifecycle.RegisterCleanup(
                "NATS observer connection",
                _ =>
                {
                    var observer = Interlocked.Exchange(ref fixture._observer, null);
                    return observer is null ? ValueTask.CompletedTask : observer.DisposeAsync();
                }
            );
            await fixture._lifecycle.RunCancellablePhaseAsync(
                "observer connection",
                _ => fixture._observer.ConnectAsync().AsTask(),
                TimeSpan.FromSeconds(5),
                ct
            );

            fixture._observedMessages = await fixture._lifecycle.RunCancellableOwnedPhaseAsync(
                "observer subscription",
                phaseCt =>
                    fixture
                        ._observer.SubscribeCoreAsync<byte[]>(
                            "travel.ai.>",
                            cancellationToken: phaseCt
                        )
                        .AsTask(),
                static (subscription, _) => subscription.DisposeAsync(),
                TimeSpan.FromSeconds(5),
                ct
            );
            fixture._lifecycle.RegisterCleanup(
                "NATS observer subscription",
                _ =>
                {
                    var subscription = Interlocked.Exchange(ref fixture._observedMessages, null);
                    return subscription is null
                        ? ValueTask.CompletedTask
                        : subscription.DisposeAsync();
                }
            );
            await fixture._lifecycle.RunCancellablePhaseAsync(
                "observer flush",
                phaseCt => fixture._observer.PingAsync(phaseCt).AsTask(),
                TimeSpan.FromSeconds(5),
                ct
            );

            var connectionString = fixture._postgres.GetConnectionString();

            fixture._aiHost = await fixture._lifecycle.RunOwnedPhaseAsync(
                "AI host startup",
                () =>
                    AlbaHost.For<TravelAiApp::Program>(builder =>
                    {
                        builder.UseSetting("ConnectionStrings:travel", connectionString);
                        builder.UseSetting("ConnectionStrings:nats", natsUrl);
                        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
                        builder.ConfigureLogging(logging => logging.ClearProviders());
                        builder.ConfigureServices(services =>
                        {
                            services.RemoveAll<IChatClient>();
                            services.AddSingleton<IChatClient>(new DeterministicChatClient());
                        });
                    }),
                static (host, _) => host.DisposeAsync(),
                TimeSpan.FromSeconds(12),
                ct
            );
            fixture._lifecycle.RegisterCleanup(
                "AI host",
                _ =>
                {
                    var aiHost = Interlocked.Exchange(ref fixture._aiHost, null);
                    return aiHost is null ? ValueTask.CompletedTask : aiHost.DisposeAsync();
                }
            );

            await fixture._lifecycle.RunCancellablePhaseAsync(
                "AI migrations",
                async phaseCt =>
                {
                    await using var scope = fixture._aiHost.Services.CreateAsyncScope();
                    var db =
                        scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();
                    await db.Database.MigrateAsync(phaseCt);
                },
                TimeSpan.FromSeconds(10),
                ct
            );

            fixture._host = await fixture._lifecycle.RunOwnedPhaseAsync(
                "Host startup",
                () =>
                    AlbaHost.For<TravelHostApp::Program>(builder =>
                    {
                        builder.UseSetting("ConnectionStrings:travel", connectionString);
                        builder.UseSetting("ConnectionStrings:nats", natsUrl);
                        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
                        builder.ConfigureLogging(logging => logging.ClearProviders());
                    }),
                static (host, _) => host.DisposeAsync(),
                TimeSpan.FromSeconds(12),
                ct
            );
            fixture._lifecycle.RegisterCleanup(
                "Host",
                _ =>
                {
                    var host = Interlocked.Exchange(ref fixture._host, null);
                    return host is null ? ValueTask.CompletedTask : host.DisposeAsync();
                }
            );

            return fixture;
        }
        catch (Exception exception)
        {
            var diagnostics = await TransportFixtureLifecycle.CaptureDiagnosticsAsync(
                "NATS logs",
                async diagnosticCt =>
                {
                    var (stdout, stderr) = await fixture._nats.GetLogsAsync(
                        DateTime.UnixEpoch,
                        DateTime.UtcNow,
                        timestampsEnabled: false,
                        diagnosticCt
                    );
                    return $"NATS stdout: {stdout} NATS stderr: {stderr}";
                },
                DiagnosticTimeout,
                CancellationToken.None
            );
            var cleanupErrors = await fixture._lifecycle.DisposeBestEffortAsync();
            var cleanupSummary =
                cleanupErrors.Count == 0
                    ? string.Empty
                    : $" Cleanup also reported: {string.Join(" | ", cleanupErrors.Select(error => error.Message))}";
            throw new InvalidOperationException(
                $"Transport fixture startup failed. {diagnostics}{cleanupSummary}",
                exception
            );
        }
    }

    private static async Task StartContainerAsync(
        IContainer container,
        string name,
        CancellationToken ct
    )
    {
        try
        {
            await container.StartAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{name} container did not become ready.",
                exception
            );
        }
    }

    public async Task<HealthyTransportEvidence> InvokeHealthyAsync(
        NlSearchRequested request,
        CancellationToken ct
    )
    {
        var host = _host ?? throw new ObjectDisposedException(GetType().Name);
        var observedMessages =
            _observedMessages ?? throw new ObjectDisposedException(GetType().Name);

        using var healthyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        healthyCts.CancelAfter(TimeSpan.FromSeconds(8));
        var observedTask = observedMessages.Msgs.ReadAsync(healthyCts.Token).AsTask();

        await using var scope = host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var reply = await bus.InvokeAsync<NlSearchParsed>(
            request,
            healthyCts.Token,
            timeout: TimeSpan.FromSeconds(8)
        );
        var observed = await observedTask;

        return new HealthyTransportEvidence(reply, observed.Subject);
    }

    public async Task<IReadOnlyList<LedgerEvidence>> ReadLedgerRowsAsync(
        Guid correlationId,
        CancellationToken ct
    )
    {
        var aiHost = _aiHost ?? throw new ObjectDisposedException(GetType().Name);
        await using var scope = aiHost.Services.CreateAsyncScope();
        var db =
            scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();

        return await db
            .CostLedger.AsNoTracking()
            .Where(entry => entry.CorrelationId == correlationId)
            .Select(entry => new LedgerEvidence(entry.MessageIdentity, entry.CorrelationId))
            .ToListAsync(ct);
    }

    public async Task StopAiAsync(CancellationToken ct)
    {
        var aiHost = Interlocked.Exchange(ref _aiHost, null);
        if (aiHost is not null)
        {
            await _lifecycle.RunCancellablePhaseAsync(
                "AI host shutdown",
                _ => aiHost.DisposeAsync().AsTask(),
                TimeSpan.FromSeconds(5),
                ct
            );
        }
    }

    public async Task<ErrorOr<SearchResult>> InvokeHostHandlerWithoutAiAsync(CancellationToken ct)
    {
        var host = _host ?? throw new ObjectDisposedException(GetType().Name);
        using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fallbackCts.CancelAfter(TimeSpan.FromSeconds(8));

        await using var scope = host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        return await bus.InvokeAsync<ErrorOr<SearchResult>>(
            new NlSearchQuery("Хочу улететь из Москвы в Петербург завтра"),
            fallbackCts.Token,
            timeout: TimeSpan.FromSeconds(8)
        );
    }

    public ValueTask DisposeAsync() => _lifecycle.DisposeAsync();

    public sealed record HealthyTransportEvidence(NlSearchParsed Reply, string ObservedSubject);

    public sealed record LedgerEvidence(string MessageIdentity, Guid CorrelationId);

    private sealed class DeterministicChatClient : IChatClient
    {
        private const string ResponseJson = """
            {
              "origin": "LED",
              "destination": "DME",
              "departure_date": "2026-09-15",
              "return_date": null,
              "passenger_count": 1,
              "cabin_class": "economy",
              "currency": "RUB"
            }
            """;

        public ChatClientMetadata Metadata => new("deterministic", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseJson))
            {
                ModelId = "transport-test-model",
                Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 8 },
            };
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("Streaming is not used by NL search.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
