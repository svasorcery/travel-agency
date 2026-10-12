using System.Security.Cryptography;
using Anthropic;
using Microsoft.Extensions.AI;
using Travel.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;

namespace Travel.Tests.AiEvals.Flights.FareSummary;

public sealed record FareSummaryPaidCaseReceipt(
    FareSummaryEvalOutcome Outcome,
    string? ActualModel,
    int? InputTokens,
    int? OutputTokens,
    int EstimatedInputTokens
);

public sealed record FareSummaryPaidReceipt(
    string PacketSha256,
    string PromptVersion,
    int RenderVersion,
    string ConfiguredModel,
    decimal InputUsdPerMillion,
    decimal OutputUsdPerMillion,
    decimal EstimatedSpendUpperUsd,
    decimal? ActualSpendUsd,
    FareSummaryPaidCaseReceipt[] Cases
);

// This is deliberately not an xUnit test. A CI Anthropic key alone cannot invoke it.
// Running it incurs cost and requires separate human authorization, explicit flag,
// a separate authorization environment token, a key, and verified exact model pricing.
public static class FareSummaryPaidEvalRunner
{
    public const string AuthorizationEnvironmentVariable =
        "TRAVEL_FARE_SUMMARY_PAID_EVAL_AUTHORIZATION";
    public const string AuthorizationToken = "APPROVED_FICTIONAL_FARE_SUMMARY_EVAL";

    public static void ValidateAuthorization(
        bool authorized,
        string? authorizationToken,
        string? apiKey,
        FareSummaryOptions options
    )
    {
        if (
            !authorized
            || authorizationToken != AuthorizationToken
            || string.IsNullOrWhiteSpace(apiKey)
            || options is null
            || !options.Enabled
            || string.IsNullOrWhiteSpace(options.Model)
            || options.Model.Length > 128
            || options.PricingModel != options.Model
            || !options.PricingVerified
            || options.InputUsdPerMillion <= 0
            || options.OutputUsdPerMillion <= 0
        )
            throw new InvalidOperationException(
                "Paid fare-summary evaluation requires explicit separate authorization and verified configuration."
            );
    }

    private sealed class CountingClient(IChatClient inner) : IChatClient
    {
        public int Calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return inner.GetResponseAsync(messages, options, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    public static async Task<FareSummaryPaidReceipt> RunAsync(
        string manifestPath,
        FareSummaryOptions options,
        string apiKey,
        TimeProvider time,
        bool authorized = false,
        CancellationToken cancellationToken = default
    )
    {
        ValidateAuthorization(
            authorized,
            Environment.GetEnvironmentVariable(AuthorizationEnvironmentVariable),
            apiKey,
            options
        );
        var manifest = FareSummaryEvalPacket.Load(manifestPath);
        FareSummaryEvalPacket.ValidateManifest(manifest);
        var holdout = manifest.Cases.Where(c => c.Partition == "Holdout").ToArray();
        var estimates = holdout
            .Where(c => c.Eligible)
            .ToDictionary(
                c => c.Id,
                c =>
                    FareSummaryExtractor.EstimateInput(
                        FareSummaryEvalPacket.Request(
                            c,
                            time.GetUtcNow(),
                            injectCatalogFaults: false
                        )
                    ),
                StringComparer.Ordinal
            );
        if (estimates.Count < 12 || estimates.Values.Any(e => !e.WithinBounds))
            throw new InvalidOperationException(
                "The preselected eligible holdout does not fit the actual source catalog."
            );
        decimal estimatedSpend = 0;
        foreach (var estimate in estimates.Values)
        {
            var cost =
                (
                    estimate.EstimatedInputTokens * options.InputUsdPerMillion
                    + FareSummaryExtractor.MaxOutputTokens * options.OutputUsdPerMillion
                ) / 1_000_000m;
            if (cost > FareSummaryExtractor.MaxCostUsd)
                throw new InvalidOperationException(
                    "The preselected model price exceeds the request budget."
                );
            estimatedSpend += cost;
        }
        using var sdkChat = new AnthropicClient(
            FareSummaryChatClient.CreateClientOptions(apiKey)
        ).AsIChatClient(options.Model);
        using var chat = new CountingClient(sdkChat);
        var extractor = new FareSummaryExtractor(chat, options, new(time), time);
        var receipts = new List<FareSummaryPaidCaseReceipt>();
        var first = true;
        foreach (var c in holdout)
        {
            if (!c.Eligible)
            {
                // Ineligible cases are expected refusal/no-data proofs, never model successes.
                receipts.Add(
                    new(new(c.Id, c.ExpectedStatus, 0, null, 0, true, true, 0), null, null, null, 0)
                );
                continue;
            }
            // Offline runner pacing is outside optional request latency. Runtime has no queue.
            if (!first)
                await Task.Delay(TimeSpan.FromSeconds(11), time, cancellationToken);
            first = false;
            var request = FareSummaryEvalPacket.Request(
                c,
                time.GetUtcNow(),
                injectCatalogFaults: false
            );
            var started = time.GetTimestamp();
            var callsBefore = chat.Calls;
            var reply = await extractor.ExtractAsync(request, cancellationToken);
            var snapshot = FareSummaryEvalPacket.Snapshot(c)!;
            var valid =
                reply.Status == "Ready"
                && !FareSummaryPlanValidator.Validate(request, reply).IsError;
            var rendered = FareSummaryFormatter.Current(
                snapshot,
                FareSummaryEvalPacket.Now,
                hasSelectedServices: true,
                plan: valid ? reply.Sections : null
            );
            var complete =
                rendered.Passengers.Length == snapshot.Evidence.Passengers.Count
                && rendered.MissingInformation.Length >= 4
                && rendered
                    .Sections.SelectMany(s => s.Lines)
                    .SelectMany(l => l.FactIds)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == request.Facts.Length;
            var status = reply.Status == "Ready" && !valid ? "InvalidOutput" : reply.Status;
            // All wording, money, scope and mandatory notices come from the formatter;
            // arbitrary model text is never displayed. This is not a production Host latency probe.
            var outcome = new FareSummaryEvalOutcome(
                c.Id,
                status,
                chat.Calls - callsBefore,
                reply.CostUsd,
                time.GetElapsedTime(started).TotalSeconds,
                true,
                complete,
                0
            );
            receipts.Add(
                new(
                    outcome,
                    reply.ModelId,
                    reply.InputTokens,
                    reply.OutputTokens,
                    estimates[c.Id].EstimatedInputTokens
                )
            );
        }
        var known = receipts
            .Where(r => r.Outcome.Dispatches > 0)
            .All(r => r.Outcome.CostUsd is not null);
        decimal? actual = known ? receipts.Sum(r => r.Outcome.CostUsd ?? 0m) : null;
        return new(
            Convert
                .ToHexString(
                    SHA256.HashData(await File.ReadAllBytesAsync(manifestPath, cancellationToken))
                )
                .ToLowerInvariant(),
            manifest.PromptVersion,
            manifest.RenderVersion,
            options.Model,
            options.InputUsdPerMillion,
            options.OutputUsdPerMillion,
            estimatedSpend,
            actual,
            receipts.ToArray()
        );
    }
}
