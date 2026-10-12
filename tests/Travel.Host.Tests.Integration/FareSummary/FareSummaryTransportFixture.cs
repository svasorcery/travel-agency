extern alias TravelAiApp;
using System.Collections.Concurrent;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using Travel.Host.Tests.Integration.NlSearch;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Nats;
using AiAdmission = TravelAiApp::Travel.AI.FareSummary.FareSummaryAdmission;
using AiDeliveryPolicy = TravelAiApp::Travel.AI.FareSummary.FareSummaryDeliveryPolicy;
using AiExtractor = TravelAiApp::Travel.AI.FareSummary.FareSummaryExtractor;
using AiHandler = TravelAiApp::Travel.AI.FareSummary.FareSummaryAiHandler;
using AiLedger = TravelAiApp::Travel.AI.FareSummary.IFareSummaryLedger;
using AiOptions = TravelAiApp::Travel.AI.FareSummary.FareSummaryOptions;

namespace Travel.Host.Tests.Integration.FareSummary;

// Source-only CI fixture: Core NATS plus production extractor/handler/delivery policy.
// It intentionally does not bootstrap Host/AI Program, SQL, keys or paid clients.
public sealed class FareSummaryTransportFixture : IAsyncDisposable
{
    private readonly TransportFixtureLifecycle lifecycle = new(TimeSpan.FromSeconds(3));
    private readonly IContainer nats = new ContainerBuilder("library/nats:2.12")
        .WithPortBinding(4222, true)
        .WithPortBinding(8222, true)
        .WithCommand("-m", "8222")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .Build();
    private readonly List<IHost> replicas = [];
    private readonly List<FareTransportProbe> probes = [];
    private readonly HttpClient monitor = new() { Timeout = Timeout.InfiniteTimeSpan };
    private IHost? publisher;
    private NatsConnection? observer;
    private INatsSub<byte[]>? observed;
    public IReadOnlyList<FareTransportProbe> Probes => probes;
    public string Fault { get; private set; } = "None";

    private FareSummaryTransportFixture()
    {
        lifecycle.RegisterCleanup("NATS", _ => nats.DisposeAsync());
        lifecycle.RegisterCleanup(
            "monitor",
            _ =>
            {
                monitor.Dispose();
                return ValueTask.CompletedTask;
            }
        );
    }

    public static async Task<FareSummaryTransportFixture> StartAsync(
        string fault,
        CancellationToken ct
    )
    {
        var f = new FareSummaryTransportFixture { Fault = fault };
        try
        {
            await f.lifecycle.RunCancellablePhaseAsync(
                "NATS startup",
                token => f.nats.StartAsync(token),
                TimeSpan.FromSeconds(20),
                ct
            );
            var url = $"nats://{f.nats.Hostname}:{f.nats.GetMappedPublicPort(4222)}";
            f.monitor.BaseAddress = new Uri(
                $"http://{f.nats.Hostname}:{f.nats.GetMappedPublicPort(8222)}/"
            );
            f.observer = new NatsConnection(new NatsOpts { Url = url });
            f.lifecycle.RegisterCleanup("observer", _ => f.observer.DisposeAsync());
            await f.lifecycle.RunCancellablePhaseAsync(
                "observer connection",
                _ => f.observer.ConnectAsync().AsTask(),
                TimeSpan.FromSeconds(5),
                ct
            );
            f.observed = await f.observer.SubscribeCoreAsync<byte[]>(
                FareSummaryMessageIdentity.Subject,
                cancellationToken: ct
            );
            f.lifecycle.RegisterCleanup("observer subscription", _ => f.observed.DisposeAsync());
            await f.observer.PingAsync(ct);
            for (var i = 0; i < 2; i++)
            {
                var probe = new FareTransportProbe($"fictional-replica-{i + 1}", fault);
                f.probes.Add(probe);
                var host = await f.StartHostAsync(url, probe, ct);
                f.replicas.Add(host);
            }
            await f.WaitForMembershipAsync(2, ct);
            f.publisher = await f.StartHostAsync(url, null, ct);
            return f;
        }
        catch
        {
            await f.DisposeAsync();
            throw;
        }
    }

    private async Task<IHost> StartHostAsync(
        string url,
        FareTransportProbe? probe,
        CancellationToken ct
    )
    {
        var host = await lifecycle.RunOwnedPhaseAsync(
            "Wolverine test host",
            async () =>
            {
                var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                if (probe is not null)
                {
                    builder.Services.AddSingleton(probe);
                    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
                    builder.Services.AddSingleton<AiLedger>(probe);
                    builder.Services.AddSingleton<IChatClient>(probe);
                    builder.Services.AddSingleton(
                        new AiOptions
                        {
                            Enabled = true,
                            Model = probe.ModelId,
                            PricingModel = probe.ModelId,
                            PricingVerified = true,
                            InputUsdPerMillion = 1,
                            OutputUsdPerMillion = 1,
                        }
                    );
                    builder.Services.AddSingleton<AiAdmission>();
                    builder.Services.AddSingleton<AiExtractor>();
                }
                builder.UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.UseNats(url);
                    if (probe is null)
                        options
                            .PublishMessage<FareSummaryRequested>()
                            .ToNatsSubject(FareSummaryMessageIdentity.Subject);
                    else
                    {
                        options.Discovery.IncludeType<FareTransportTestHandler>();
                        options
                            .ListenToNatsSubject(FareSummaryMessageIdentity.Subject)
                            .UseQueueGroup(FareSummaryMessageIdentity.QueueGroup);
                        options.Policies.Add<AiDeliveryPolicy>();
                    }
                });
                var built = builder.Build();
                try
                {
                    await built.StartAsync(ct);
                    return built;
                }
                catch
                {
                    built.Dispose();
                    throw;
                }
            },
            static (host, _) =>
            {
                host.Dispose();
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromSeconds(10),
            ct
        );
        lifecycle.RegisterCleanup(
            "Wolverine test host",
            async token =>
            {
                await host.StopAsync(token);
                host.Dispose();
            }
        );
        return host;
    }

    public async Task WaitForMembershipAsync(int expected, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var json = await monitor.GetStringAsync(
                "subsz?subs=1&test=travel.ai.fare_summary&limit=1024",
                deadline.Token
            );
            if (json.Length > 1_000_000)
                throw new InvalidOperationException("NATS diagnostic bound exceeded.");
            using var doc = JsonDocument.Parse(json);
            var entries = doc.RootElement.TryGetProperty("subscriptions_list", out var list)
                ? list.EnumerateArray()
                    .Where(x =>
                        x.GetProperty("subject").GetString() == FareSummaryMessageIdentity.Subject
                        && x.TryGetProperty("qgroup", out var group)
                        && !string.IsNullOrEmpty(group.GetString())
                    )
                    .ToArray()
                : [];
            if (
                entries.Length == expected
                && entries.All(x =>
                    x.GetProperty("qgroup").GetString() == FareSummaryMessageIdentity.QueueGroup
                )
                && entries.Select(x => x.GetProperty("cid").GetInt64()).Distinct().Count()
                    == expected
            )
                return;
            await Task.Delay(25, deadline.Token);
        }
    }

    public async Task<(FareSummaryPlanned Reply, string Subject)> InvokeAsync(
        FareSummaryRequested request,
        CancellationToken ct
    )
    {
        var observation = observed!.Msgs.ReadAsync(ct).AsTask();
        var reply = await publisher!
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<FareSummaryPlanned>(request, ct, TimeSpan.FromSeconds(2));
        return (reply, (await observation).Subject);
    }

    public async Task StopRespondersAsync(CancellationToken ct)
    {
        foreach (var replica in replicas)
            await replica.StopAsync(ct);
        await WaitForMembershipAsync(0, ct);
    }

    public Task<FareSummaryResult> OrganizeWithoutConsumersAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var condition = FareCondition
            .Create(FarePermission.Allowed, null, "Known", "NotProvided")
            .Value;
        var evidence = FareRuleEvidence
            .Create(
                now,
                now.AddMinutes(1),
                now.AddDays(1),
                1,
                [1],
                condition,
                FareCondition.NotProvided,
                [new(0, null, FareCondition.NotProvided)],
                [new(0, 0, 0, null, null, null, null)]
            )
            .Value;
        var snapshot = JsonSerializer.Deserialize<QuotedFareSnapshot>(
            JsonSerializer.Serialize(
                new
                {
                    AggregateId = Guid.NewGuid(),
                    OfferId = Guid.NewGuid(),
                    QuoteRevision = Guid.NewGuid(),
                    FingerprintVersion = 1,
                    Fingerprint = new string('a', 64),
                    Evidence = evidence,
                }
            )
        )!;
        var organizer = new FareSummaryOrganizer(
            publisher!.Services.GetRequiredService<IMessageBus>(),
            TimeProvider.System,
            new() { OrganizedEnabled = true },
            NullLogger<FareSummaryOrganizer>.Instance
        );
        return organizer.OrganizeAsync(
            snapshot,
            "organized",
            true,
            false,
            now,
            _ => Task.FromResult(true),
            ct
        );
    }

    public ValueTask DisposeAsync() => lifecycle.DisposeAsync();
}

public sealed class FareTransportProbe(string modelId, string fault) : IChatClient, AiLedger
{
    public string ModelId { get; } = modelId;
    public string Fault { get; } = fault;
    public ConcurrentQueue<FareSummaryPlanned> Ledger { get; } = new();
    private int calls;
    private int deliveries;
    public int Deliveries => Volatile.Read(ref deliveries);

    public void Delivered() => Interlocked.Increment(ref deliveries);

    public int Calls => Volatile.Read(ref calls);

    public Task RecordAsync(FareSummaryPlanned result, CancellationToken ct)
    {
        Ledger.Enqueue(result);
        return Task.CompletedTask;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref calls);
        const string output =
            "{\"sections\":[{\"kind\":\"change\",\"groups\":[{\"factIds\":[\"f0\"]},{\"factIds\":[\"f2\"]}]},{\"kind\":\"refund\",\"groups\":[{\"factIds\":[\"f1\"]}]}]}";
        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, output))
            {
                ModelId = ModelId,
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
            }
        );
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public object? GetService(Type type, object? key = null) => null;

    public void Dispose() { }
}

public sealed class FareTransportTestHandler
{
    [WolverineHandler]
    public static async Task<FareSummaryPlanned> Handle(
        FareSummaryRequested request,
        AiExtractor extractor,
        AiLedger ledger,
        TimeProvider time,
        FareTransportProbe probe,
        IMessageContext context,
        CancellationToken ct
    )
    {
        probe.Delivered();
        if (probe.Fault == "AfterInference")
        {
            await extractor.ExtractAsync(request, ct);
            throw new InvalidOperationException("Fictional fault after possible inference.");
        }
        var result = await AiHandler.Handle(
            request,
            extractor,
            ledger,
            time,
            NullLogger<FareSummaryRequested>.Instance,
            ct
        );
        if (probe.Fault == "AfterAccounting")
            throw new InvalidOperationException("Fictional fault after accounting.");
        if (probe.Fault == "AfterDelivery")
        {
            await context.RespondToSenderAsync(result);
            throw new InvalidOperationException("Fictional fault after possible delivery.");
        }
        return result;
    }
}
