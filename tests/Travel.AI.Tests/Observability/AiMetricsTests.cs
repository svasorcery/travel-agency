using System.Diagnostics.Metrics;
using Shouldly;
using Travel.AI.Observability;
using Xunit;

namespace Travel.AI.Tests.Observability;

/// <summary>
/// Verifies that <see cref="AiMetrics"/> emits OTel GenAI-semconv measurements on the
/// expected instruments. Uses a raw <see cref="MeterListener"/> — no extra package required.
/// </summary>
public sealed class AiMetricsTests : IDisposable
{
    private readonly TestMeterFactory _factory = new();
    private readonly AiMetrics _sut;

    public AiMetricsTests() => _sut = new AiMetrics(_factory);

    public void Dispose() => _factory.Dispose();

    private List<(double Value, IEnumerable<KeyValuePair<string, object?>> Tags)> Collect(
        string instrumentName,
        Action act
    )
    {
        var results = new List<(double, IEnumerable<KeyValuePair<string, object?>>)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AiMetrics.MeterName && instrument.Name == instrumentName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>(
            (instr, value, tags, _) =>
            {
                if (instr.Name == instrumentName)
                    results.Add((value, tags.ToArray()));
            }
        );
        listener.SetMeasurementEventCallback<long>(
            (instr, value, tags, _) =>
            {
                if (instr.Name == instrumentName)
                    results.Add(((double)value, tags.ToArray()));
            }
        );

        listener.Start();
        act();
        listener.RecordObservableInstruments();
        return results;
    }

    [Fact]
    public void RecordTokenUsage_EmitsOneMeasurementPerTokenType_WithModelAndOperationTags()
    {
        var measurements = Collect(
            "gen_ai.client.token.usage",
            () =>
                _sut.RecordTokenUsage("claude-opus-4-7", "chat", inputTokens: 100, outputTokens: 50)
        );

        measurements.Count.ShouldBe(2);

        var input = measurements.Single(m =>
            m.Tags.Any(t => t.Key == "gen_ai.token.type" && (string?)t.Value == "input")
        );
        input.Value.ShouldBe(100);

        var output = measurements.Single(m =>
            m.Tags.Any(t => t.Key == "gen_ai.token.type" && (string?)t.Value == "output")
        );
        output.Value.ShouldBe(50);

        foreach (var m in measurements)
        {
            m.Tags.ShouldContain(t =>
                t.Key == "gen_ai.request.model" && (string?)t.Value == "claude-opus-4-7"
            );
            m.Tags.ShouldContain(t =>
                t.Key == "gen_ai.operation.name" && (string?)t.Value == "chat"
            );
        }
    }

    [Fact]
    public void RecordOperationDuration_EmitsMeasurement_WithModelAndOperationTags()
    {
        var measurements = Collect(
            "gen_ai.client.operation.duration",
            () => _sut.RecordOperationDuration("claude-opus-4-7", "chat", 1.25)
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(1.25);
        measurements[0]
            .Tags.ShouldContain(t =>
                t.Key == "gen_ai.request.model" && (string?)t.Value == "claude-opus-4-7"
            );
        measurements[0]
            .Tags.ShouldContain(t =>
                t.Key == "gen_ai.operation.name" && (string?)t.Value == "chat"
            );
    }
}

/// <summary>Minimal <see cref="IMeterFactory"/> shim for unit testing without a DI container.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var m = new Meter(options.Name, options.Version);
        _meters.Add(m);
        return m;
    }

    public void Dispose()
    {
        foreach (var m in _meters)
            m.Dispose();
    }
}
