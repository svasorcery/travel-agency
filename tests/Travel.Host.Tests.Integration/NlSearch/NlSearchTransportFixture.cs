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

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("travel_transport_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly IContainer _nats = new ContainerBuilder("library/nats:2.12")
        .WithPortBinding(NatsPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .Build();

    private NatsConnection? _observer;
    private INatsSub<byte[]>? _observedMessages;
    private IAlbaHost? _aiHost;
    private IAlbaHost? _host;

    private NlSearchTransportFixture() { }

    public static async Task<NlSearchTransportFixture> StartAsync(CancellationToken ct)
    {
        var fixture = new NlSearchTransportFixture();
        try
        {
            await RunPhaseAsync(
                "containers",
                () =>
                    Task.WhenAll(
                        StartContainerAsync(fixture._postgres, "PostgreSQL", ct),
                        StartContainerAsync(fixture._nats, "Core NATS", ct)
                    ),
                TimeSpan.FromSeconds(15),
                ct
            );

            var natsUrl =
                $"nats://{fixture._nats.Hostname}:{fixture._nats.GetMappedPublicPort(NatsPort)}";
            fixture._observer = new NatsConnection(new NatsOpts { Url = natsUrl });
            await RunPhaseAsync(
                "observer",
                async () =>
                {
                    await fixture._observer.ConnectAsync();
                    fixture._observedMessages = await fixture._observer.SubscribeCoreAsync<byte[]>(
                        "travel.ai.>",
                        cancellationToken: ct
                    );
                    await fixture._observer.PingAsync(ct);
                },
                TimeSpan.FromSeconds(5),
                ct
            );

            var connectionString = fixture._postgres.GetConnectionString();

            fixture._aiHost = await RunPhaseAsync(
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
                TimeSpan.FromSeconds(12),
                ct
            );

            await RunPhaseAsync(
                "AI migrations",
                async () =>
                {
                    await using var scope = fixture._aiHost.Services.CreateAsyncScope();
                    var db =
                        scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();
                    await db.Database.MigrateAsync(ct);
                },
                TimeSpan.FromSeconds(10),
                ct
            );

            fixture._host = await RunPhaseAsync(
                "Host startup",
                () =>
                    AlbaHost.For<TravelHostApp::Program>(builder =>
                    {
                        builder.UseSetting("ConnectionStrings:travel", connectionString);
                        builder.UseSetting("ConnectionStrings:nats", natsUrl);
                        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
                        builder.ConfigureLogging(logging => logging.ClearProviders());
                    }),
                TimeSpan.FromSeconds(12),
                ct
            );

            return fixture;
        }
        catch (Exception exception)
        {
            var (stdout, stderr) = await fixture._nats.GetLogsAsync(
                DateTime.UnixEpoch,
                DateTime.UtcNow,
                timestampsEnabled: false,
                CancellationToken.None
            );
            await fixture.DisposeAsync();
            throw new InvalidOperationException(
                $"Transport fixture startup failed. NATS stdout: {stdout} NATS stderr: {stderr}",
                exception
            );
        }
    }

    private static async Task RunPhaseAsync(
        string phase,
        Func<Task> action,
        TimeSpan timeout,
        CancellationToken ct
    ) => await RunPhaseAsync(() => phase, action, timeout, ct);

    private static async Task RunPhaseAsync(
        Func<string> phase,
        Func<Task> action,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        try
        {
            await action().WaitAsync(timeout, ct);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Transport fixture timed out during {phase()}.", exception);
        }
    }

    private static async Task<T> RunPhaseAsync<T>(
        string phase,
        Func<Task<T>> action,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        try
        {
            return await action().WaitAsync(timeout, ct);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Transport fixture timed out during {phase}.", exception);
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
            await aiHost.DisposeAsync().AsTask().WaitAsync(ct);
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

    public async ValueTask DisposeAsync()
    {
        var host = Interlocked.Exchange(ref _host, null);
        if (host is not null)
            await host.DisposeAsync();

        var aiHost = Interlocked.Exchange(ref _aiHost, null);
        if (aiHost is not null)
            await aiHost.DisposeAsync();

        if (_observedMessages is not null)
        {
            await _observedMessages.DisposeAsync();
            _observedMessages = null;
        }

        if (_observer is not null)
        {
            await _observer.DisposeAsync();
            _observer = null;
        }

        await _nats.DisposeAsync();
        await _postgres.DisposeAsync();
    }

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
