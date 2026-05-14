using System.Diagnostics.Metrics;

namespace Travel.AI.Observability;

/// <summary>
/// OTel meter for the Travel.AI service, following the GenAI semantic conventions
/// (<c>gen_ai.*</c>). Singleton; the meter is registered with the OTLP exporter in
/// <c>Program.cs</c> via <c>AddMeter(AiMetrics.MeterName)</c>.
/// </summary>
public sealed class AiMetrics
{
    public const string MeterName = "Travel.AI";

    private readonly Counter<long> _tokenUsage;
    private readonly Histogram<double> _operationDuration;

    public AiMetrics(IMeterFactory factory)
    {
        var m = factory.Create(MeterName);

        _tokenUsage = m.CreateCounter<long>(
            "gen_ai.client.token.usage",
            unit: "{token}",
            description: "Number of tokens used in a GenAI model call."
        );
        _operationDuration = m.CreateHistogram<double>(
            "gen_ai.client.operation.duration",
            unit: "s",
            description: "Wall-clock duration of a GenAI model call."
        );
    }

    /// <summary>
    /// Records token consumption for one model call. Emits <c>gen_ai.client.token.usage</c>
    /// once per token type (<c>input</c> / <c>output</c>) per GenAI semconv.
    /// </summary>
    public void RecordTokenUsage(string model, string operation, int inputTokens, int outputTokens)
    {
        _tokenUsage.Add(
            inputTokens,
            new KeyValuePair<string, object?>("gen_ai.token.type", "input"),
            new KeyValuePair<string, object?>("gen_ai.request.model", model),
            new KeyValuePair<string, object?>("gen_ai.operation.name", operation)
        );
        _tokenUsage.Add(
            outputTokens,
            new KeyValuePair<string, object?>("gen_ai.token.type", "output"),
            new KeyValuePair<string, object?>("gen_ai.request.model", model),
            new KeyValuePair<string, object?>("gen_ai.operation.name", operation)
        );
    }

    /// <summary>
    /// Records the wall-clock duration of one model call as <c>gen_ai.client.operation.duration</c>.
    /// </summary>
    public void RecordOperationDuration(string model, string operation, double seconds) =>
        _operationDuration.Record(
            seconds,
            new KeyValuePair<string, object?>("gen_ai.request.model", model),
            new KeyValuePair<string, object?>("gen_ai.operation.name", operation)
        );
}
