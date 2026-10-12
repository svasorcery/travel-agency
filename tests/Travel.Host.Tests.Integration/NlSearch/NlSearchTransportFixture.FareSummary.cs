extern alias TravelAiApp;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine;
using AiAdmission = TravelAiApp::Travel.AI.FareSummary.FareSummaryAdmission;
using AiEfLedger = TravelAiApp::Travel.AI.FareSummary.FareSummaryLedger;
using AiExtractor = TravelAiApp::Travel.AI.FareSummary.FareSummaryExtractor;
using AiLedger = TravelAiApp::Travel.AI.FareSummary.IFareSummaryLedger;
using AiOptions = TravelAiApp::Travel.AI.FareSummary.FareSummaryOptions;

namespace Travel.Host.Tests.Integration.NlSearch;

// Opt-in extension of the existing full production Programs/SQL disposable fixture.
// Only the model boundary is fictional; IFareSummaryLedger remains the production EF service.
public sealed partial class NlSearchTransportFixture
{
    private readonly bool _fareEnabled;
    private readonly FarePipelineChat _fareOne = new("fare-transport-replica-1");
    private readonly FarePipelineChat _fareTwo = new("fare-transport-replica-2");

    private void ConfigureFarePipeline(IServiceCollection services, string replicaModel)
    {
        var chat = replicaModel == DeterministicChatClient.ReplicaOneModelId ? _fareOne : _fareTwo;
        services.RemoveAll<AiExtractor>();
        services.AddSingleton(sp =>
        {
            var time = sp.GetRequiredService<TimeProvider>();
            return new AiExtractor(
                chat,
                new AiOptions
                {
                    Enabled = true,
                    Model = chat.ModelId,
                    PricingModel = chat.ModelId,
                    PricingVerified = true,
                    InputUsdPerMillion = 1,
                    OutputUsdPerMillion = 1,
                },
                new AiAdmission(time),
                time
            );
        });
    }

    public void SetFareResponseMode(string mode)
    {
        _fareOne.Mode = mode;
        _fareTwo.Mode = mode;
    }

    public int FareModelCalls => _fareOne.Calls + _fareTwo.Calls;

    public async Task AssertProductionFareLedgerAsync(CancellationToken ct)
    {
        foreach (var host in new[] { _aiReplicaOneHost, _aiReplicaTwoHost })
        {
            await using var scope = host!.Services.CreateAsyncScope();
            if (scope.ServiceProvider.GetRequiredService<AiLedger>() is not AiEfLedger)
                throw new InvalidOperationException(
                    "Fare transport must retain the production EF ledger registration."
                );
        }
    }

    public async Task<NatsListenerMembership> WaitForFareListenerMembershipAsync(
        int expected,
        CancellationToken ct
    )
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(ListenerMembershipTimeout);
        while (true)
        {
            var json = await _natsMonitorClient.GetStringAsync(
                "subsz?subs=1&test=travel.ai.fare_summary&limit=1024",
                bound.Token
            );
            if (json.Length > MaxNatsMonitoringResponseCharacters)
                throw new InvalidOperationException("NATS diagnostic bounds exceeded.");
            if (TryReadFareListenerMembership(json, expected, out var membership))
                return membership!;
            await Task.Delay(ListenerMembershipPollInterval, bound.Token);
        }
    }

    public async Task<(FareSummaryPlanned Reply, string Subject)> InvokeFareAsync(
        FareSummaryRequested request,
        CancellationToken ct
    )
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(TimeSpan.FromSeconds(8));
        var observedTask = _observedMessages!.Msgs.ReadAsync(bound.Token).AsTask();
        await using var scope = _host!.Services.CreateAsyncScope();
        var reply = await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<FareSummaryPlanned>(request, bound.Token, TimeSpan.FromSeconds(8));
        return (reply, (await observedTask).Subject);
    }

    public async Task<FareLedgerEvidence[]> ReadFareLedgerAsync(
        Guid correlation,
        CancellationToken ct
    )
    {
        var host =
            _aiReplicaOneHost
            ?? _aiReplicaTwoHost
            ?? throw new ObjectDisposedException(GetType().Name);
        await using var scope = host.Services.CreateAsyncScope();
        var db =
            scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();
        return await db
            .CostLedger.AsNoTracking()
            .Where(x => x.CorrelationId == correlation)
            .Select(x => new FareLedgerEvidence(
                x.Feature,
                x.MessageIdentity,
                x.CorrelationId,
                x.Model,
                x.InputTokens,
                x.OutputTokens,
                x.CostUsd,
                x.UserId
            ))
            .ToArrayAsync(ct);
    }

    public async Task<FareSummaryResult> InvokeFareProducerWithoutAiAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var known = FareCondition
            .Create(FarePermission.Allowed, null, "Known", "NotProvided")
            .Value;
        var evidence = FareRuleEvidence
            .Create(
                now,
                now.AddMinutes(1),
                now.AddDays(1),
                1,
                [1],
                known,
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
        await using var scope = _host!.Services.CreateAsyncScope();
        var organizer = new FareSummaryOrganizer(
            scope.ServiceProvider.GetRequiredService<IMessageBus>(),
            TimeProvider.System,
            new() { OrganizedEnabled = true },
            NullLogger<FareSummaryOrganizer>.Instance
        );
        return await organizer.OrganizeAsync(
            snapshot,
            "organized",
            true,
            false,
            now,
            _ => Task.FromResult(true),
            ct
        );
    }

    public sealed record FareLedgerEvidence(
        string Feature,
        string MessageIdentity,
        Guid CorrelationId,
        string Model,
        int InputTokens,
        int OutputTokens,
        decimal CostUsd,
        Guid? UserId
    );

    private sealed class FarePipelineChat(string modelId) : IChatClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public string ModelId => modelId;
        public string Mode { get; set; } = "Ready";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref calls);
            const string ready =
                "{\"sections\":[{\"kind\":\"change\",\"groups\":[{\"factIds\":[\"f0\"]},{\"factIds\":[\"f2\"]}]},{\"kind\":\"refund\",\"groups\":[{\"factIds\":[\"f1\"]}]}]}";
            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(
                        ChatRole.Assistant,
                        Mode == "InvalidOutput" ? "fictional invalid output" : ready
                    )
                )
                {
                    ModelId = modelId,
                    Usage =
                        Mode == "UsageUnknown"
                            ? null
                            : new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
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

    internal static bool TryReadFareListenerMembership(
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
                        FareSummaryMessageIdentity.Subject,
                        FareSummaryMessageIdentity.QueueGroup,
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
                        FareSummaryMessageIdentity.Subject,
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
                    FareSummaryMessageIdentity.QueueGroup,
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
                FareSummaryMessageIdentity.Subject,
                FareSummaryMessageIdentity.QueueGroup,
                subjectSubscriptionCount,
                subjectConnectionIds.Count
            );
            return true;
        }
    }
}
