extern alias TravelAiApp;
extern alias TravelHostApp;
using System.Text.Json;
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
    private const int NatsMonitoringPort = 8222;
    private const int MaxNatsMonitoringResponseCharacters = 1_000_000;
    private const string NlSearchSubject = "travel.ai.nl_search";
    private const string NlSearchQueueGroup = "travel.ai.nl_search.workers";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ListenerMembershipTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ListenerMembershipPollInterval = TimeSpan.FromMilliseconds(25);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("travel_transport_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly IContainer _nats = new ContainerBuilder("library/nats:2.12")
        .WithPortBinding(NatsPort, true)
        .WithPortBinding(NatsMonitoringPort, true)
        .WithCommand("-m", "8222")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .Build();

    private readonly TransportFixtureLifecycle _lifecycle = new(CleanupTimeout);
    private readonly HttpClient _natsMonitorClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };
    private readonly DeterministicChatClient _replicaOneChat = new(
        DeterministicChatClient.ReplicaOneModelId
    );
    private readonly DeterministicChatClient _replicaTwoChat = new(
        DeterministicChatClient.ReplicaTwoModelId
    );

    private NatsConnection? _observer;
    private INatsSub<byte[]>? _observedMessages;
    private IAlbaHost? _aiReplicaOneHost;
    private IAlbaHost? _aiReplicaTwoHost;
    private IAlbaHost? _host;
    private NatsListenerMembership? _aiListenerMembership;

    private NlSearchTransportFixture()
    {
        _lifecycle.RegisterCleanup("PostgreSQL", _ => _postgres.DisposeAsync());
        _lifecycle.RegisterCleanup("Core NATS", _ => _nats.DisposeAsync());
        _lifecycle.RegisterCleanup(
            "NATS monitoring client",
            _ =>
            {
                _natsMonitorClient.Dispose();
                return ValueTask.CompletedTask;
            }
        );
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
            fixture._natsMonitorClient.BaseAddress = new Uri(
                $"http://{fixture._nats.Hostname}:{fixture._nats.GetMappedPublicPort(NatsMonitoringPort)}/"
            );
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

            fixture._aiReplicaOneHost = await fixture.StartAiReplicaAsync(
                "replica 1",
                fixture._replicaOneChat,
                connectionString,
                natsUrl,
                ct
            );
            fixture._lifecycle.RegisterCleanup(
                "AI replica 1",
                _ =>
                {
                    var aiHost = Interlocked.Exchange(ref fixture._aiReplicaOneHost, null);
                    return aiHost is null ? ValueTask.CompletedTask : aiHost.DisposeAsync();
                }
            );

            await fixture._lifecycle.RunCancellablePhaseAsync(
                "AI migrations",
                async phaseCt =>
                {
                    await using var scope = fixture._aiReplicaOneHost.Services.CreateAsyncScope();
                    var db =
                        scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();
                    await db.Database.MigrateAsync(phaseCt);
                },
                TimeSpan.FromSeconds(10),
                ct
            );

            fixture._aiReplicaTwoHost = await fixture.StartAiReplicaAsync(
                "replica 2",
                fixture._replicaTwoChat,
                connectionString,
                natsUrl,
                ct
            );
            fixture._lifecycle.RegisterCleanup(
                "AI replica 2",
                _ =>
                {
                    var aiHost = Interlocked.Exchange(ref fixture._aiReplicaTwoHost, null);
                    return aiHost is null ? ValueTask.CompletedTask : aiHost.DisposeAsync();
                }
            );

            await fixture._lifecycle.RunCancellablePhaseAsync(
                "AI listener membership",
                async phaseCt =>
                {
                    fixture._aiListenerMembership = await fixture.PollForAiListenerMembershipAsync(
                        expectedSubscriptionCount: 2,
                        phaseCt
                    );
                },
                ListenerMembershipTimeout,
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

    private async Task<IAlbaHost> StartAiReplicaAsync(
        string replica,
        DeterministicChatClient chatClient,
        string connectionString,
        string natsUrl,
        CancellationToken ct
    ) =>
        await _lifecycle.RunOwnedPhaseAsync(
            $"AI {replica} startup",
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
                        services.AddSingleton<IChatClient>(chatClient);
                    });
                }),
            static (host, _) => host.DisposeAsync(),
            TimeSpan.FromSeconds(12),
            ct
        );

    private async Task<NatsListenerMembership> PollForAiListenerMembershipAsync(
        int expectedSubscriptionCount,
        CancellationToken ct
    )
    {
        while (true)
        {
            using var response = await _natsMonitorClient.GetAsync(
                "subsz?subs=1&test=travel.ai.nl_search&limit=1024",
                HttpCompletionOption.ResponseHeadersRead,
                ct
            );
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"NATS monitoring returned HTTP {(int)response.StatusCode} while checking AI listener membership."
                );
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (json.Length > MaxNatsMonitoringResponseCharacters)
            {
                throw new InvalidOperationException(
                    $"NATS monitoring response exceeded {MaxNatsMonitoringResponseCharacters} characters."
                );
            }

            if (TryReadAiListenerMembership(json, expectedSubscriptionCount, out var membership))
                return membership!;

            await Task.Delay(ListenerMembershipPollInterval, ct);
        }
    }

    internal static bool TryReadAiListenerMembership(
        string json,
        out NatsListenerMembership? membership
    ) => TryReadAiListenerMembership(json, expectedSubscriptionCount: 2, out membership);

    internal static bool TryReadAiListenerMembership(
        string json,
        int expectedSubscriptionCount,
        out NatsListenerMembership? membership
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSubscriptionCount);
        membership = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "NATS monitoring response was not valid JSON.",
                exception
            );
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "NATS monitoring response root must be an object."
                );
            }

            if (
                !root.TryGetProperty("total", out var totalProperty)
                || totalProperty.ValueKind != JsonValueKind.Number
                || !totalProperty.TryGetInt32(out var total)
                || total < 0
            )
            {
                throw new InvalidOperationException(
                    "NATS monitoring response must contain a non-negative integer total."
                );
            }

            JsonElement.ArrayEnumerator subscriptions;
            if (!root.TryGetProperty("subscriptions_list", out var subscriptionsProperty))
            {
                if (total != 0)
                {
                    throw new InvalidOperationException(
                        "NATS monitoring omitted subscriptions_list for a non-zero total."
                    );
                }

                if (expectedSubscriptionCount == 0)
                {
                    membership = new NatsListenerMembership(
                        NlSearchSubject,
                        NlSearchQueueGroup,
                        SubscriptionCount: 0,
                        DistinctConnectionCount: 0
                    );
                    return true;
                }

                return false;
            }
            else
            {
                if (subscriptionsProperty.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "NATS monitoring subscriptions_list must be an array when present."
                    );
                }

                if (subscriptionsProperty.GetArrayLength() != total)
                {
                    throw new InvalidOperationException(
                        "NATS monitoring subscriptions_list is incomplete for the reported total."
                    );
                }

                subscriptions = subscriptionsProperty.EnumerateArray();
            }

            var subjectSubscriptionCount = 0;
            var everySubjectSubscriptionUsesExpectedQueueGroup = true;
            var subjectConnectionIds = new HashSet<long>();
            foreach (var subscription in subscriptions)
            {
                if (subscription.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        "Every NATS monitoring subscription must be an object."
                    );
                }

                if (
                    !subscription.TryGetProperty("subject", out var subjectProperty)
                    || subjectProperty.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(subjectProperty.GetString())
                )
                {
                    throw new InvalidOperationException(
                        "Every NATS monitoring subscription must contain a non-empty string subject."
                    );
                }

                string? queueGroup = null;
                if (subscription.TryGetProperty("qgroup", out var queueGroupProperty))
                {
                    if (queueGroupProperty.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException(
                            "NATS monitoring qgroup must be a string when present."
                        );
                    }

                    queueGroup = queueGroupProperty.GetString();
                }

                if (
                    !string.Equals(
                        subjectProperty.GetString(),
                        NlSearchSubject,
                        StringComparison.Ordinal
                    )
                )
                    continue;

                if (
                    !subscription.TryGetProperty("cid", out var connectionIdProperty)
                    || connectionIdProperty.ValueKind != JsonValueKind.Number
                    || !connectionIdProperty.TryGetInt64(out var connectionId)
                    || connectionId < 0
                )
                {
                    throw new InvalidOperationException(
                        "Every matching NATS monitoring subscription must contain a non-negative integer cid."
                    );
                }

                subjectSubscriptionCount++;
                subjectConnectionIds.Add(connectionId);
                everySubjectSubscriptionUsesExpectedQueueGroup &= string.Equals(
                    queueGroup,
                    NlSearchQueueGroup,
                    StringComparison.Ordinal
                );
            }

            if (
                subjectSubscriptionCount != expectedSubscriptionCount
                || !everySubjectSubscriptionUsesExpectedQueueGroup
                || subjectConnectionIds.Count != subjectSubscriptionCount
            )
                return false;

            membership = new NatsListenerMembership(
                NlSearchSubject,
                NlSearchQueueGroup,
                subjectSubscriptionCount,
                subjectConnectionIds.Count
            );
            return true;
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

    public NatsListenerMembership AiListenerMembership =>
        _aiListenerMembership
        ?? throw new InvalidOperationException("AI listener membership was not established.");

    public async Task<NatsListenerMembership> WaitForAiListenerMembershipAsync(
        int expectedSubscriptionCount,
        CancellationToken ct
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSubscriptionCount);

        using var membershipCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        membershipCts.CancelAfter(ListenerMembershipTimeout);
        try
        {
            return await PollForAiListenerMembershipAsync(
                expectedSubscriptionCount,
                membershipCts.Token
            );
        }
        catch (OperationCanceledException exception)
            when (!ct.IsCancellationRequested && membershipCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting for {expectedSubscriptionCount} AI listener subscriptions.",
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
        var aiHost = _aiReplicaOneHost ?? _aiReplicaTwoHost;
        if (aiHost is null)
            throw new ObjectDisposedException(GetType().Name);

        await using var scope = aiHost.Services.CreateAsyncScope();
        var db =
            scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();

        return await db
            .CostLedger.AsNoTracking()
            .Where(entry => entry.CorrelationId == correlationId)
            .Select(entry => new LedgerEvidence(entry.MessageIdentity, entry.CorrelationId))
            .ToListAsync(ct);
    }

    public IReadOnlyDictionary<string, int> ReadReplicaCallCounts() =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [_replicaOneChat.ModelId] = _replicaOneChat.CallCount,
            [_replicaTwoChat.ModelId] = _replicaTwoChat.CallCount,
        };

    public Task StopServingReplicaAsync(string modelId, CancellationToken ct) =>
        modelId switch
        {
            DeterministicChatClient.ReplicaOneModelId => StopReplicaOneAsync(ct),
            DeterministicChatClient.ReplicaTwoModelId => StopReplicaTwoAsync(ct),
            _ => throw new ArgumentOutOfRangeException(
                nameof(modelId),
                modelId,
                "No AI replica uses the supplied model id."
            ),
        };

    public async Task StopAllAiAsync(CancellationToken ct)
    {
        await StopReplicaTwoAsync(ct);
        await StopReplicaOneAsync(ct);
    }

    private async Task StopReplicaOneAsync(CancellationToken ct)
    {
        var aiHost = Interlocked.Exchange(ref _aiReplicaOneHost, null);
        if (aiHost is not null)
        {
            await _lifecycle.RunCancellablePhaseAsync(
                "AI replica 1 shutdown",
                _ => aiHost.DisposeAsync().AsTask(),
                TimeSpan.FromSeconds(5),
                ct
            );
        }
    }

    private async Task StopReplicaTwoAsync(CancellationToken ct)
    {
        var aiHost = Interlocked.Exchange(ref _aiReplicaTwoHost, null);
        if (aiHost is not null)
        {
            await _lifecycle.RunCancellablePhaseAsync(
                "AI replica 2 shutdown",
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

    public sealed record NatsListenerMembership(
        string Subject,
        string QueueGroup,
        int SubscriptionCount,
        int DistinctConnectionCount
    );

    private sealed class DeterministicChatClient(string modelId) : IChatClient
    {
        public const string ReplicaOneModelId = "transport-test-model-replica-1";
        public const string ReplicaTwoModelId = "transport-test-model-replica-2";

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

        private int _callCount;

        public string ModelId { get; } = modelId;

        public int CallCount => Volatile.Read(ref _callCount);

        public ChatClientMetadata Metadata => new(ModelId, null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _callCount);
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseJson))
            {
                ModelId = ModelId,
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
