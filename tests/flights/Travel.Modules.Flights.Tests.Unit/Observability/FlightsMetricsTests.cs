using System.Diagnostics.Metrics;
using Shouldly;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Infrastructure.Observability;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Observability;

/// <summary>
/// Verifies that each IFlightsMetrics method emits a measurement on the expected
/// instrument. Uses a raw <see cref="MeterListener"/> — no extra package required.
/// </summary>
public sealed class FlightsMetricsTests : IDisposable
{
    private readonly MeterFactory _factory;
    private readonly FlightsMetrics _sut;

    public FlightsMetricsTests()
    {
        _factory = new MeterFactory();
        _sut = new FlightsMetrics(_factory);
    }

    public void Dispose() => _factory.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes a MeterListener to Travel.Flights, invokes <paramref name="act"/>,
    /// and returns all measurements collected for <paramref name="instrumentName"/>.
    /// </summary>
    private List<(double Value, IEnumerable<KeyValuePair<string, object?>> Tags)> Collect(
        string instrumentName,
        Action act
    )
    {
        var results = new List<(double, IEnumerable<KeyValuePair<string, object?>>)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == FlightsMetrics.MeterName
                && instrument.Name == instrumentName
            )
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

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordSearchLatency_EmitsMeasurement_WithProviderAndStatusTags()
    {
        var measurements = Collect(
            "flights.search.duration_ms",
            () => _sut.RecordSearchLatency(123.4, "duffel", "ok")
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(123.4);
        measurements[0]
            .Tags.ShouldContain(t => t.Key == "provider" && (string?)t.Value == "duffel");
        measurements[0].Tags.ShouldContain(t => t.Key == "status" && (string?)t.Value == "ok");
    }

    [Fact]
    public void RecordSearchError_EmitsCounter_WithProviderTag()
    {
        var measurements = Collect(
            "flights.search.errors",
            () => _sut.RecordSearchError("travelpayouts")
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(1);
        measurements[0]
            .Tags.ShouldContain(t => t.Key == "provider" && (string?)t.Value == "travelpayouts");
    }

    [Fact]
    public void RecordPaymentOutcome_Success_EmitsSuccessCounter()
    {
        var success = Collect(
            "flights.payment.success_total",
            () => _sut.RecordPaymentOutcome(true)
        );
        var failure = Collect(
            "flights.payment.failure_total",
            () => _sut.RecordPaymentOutcome(true)
        );

        success.ShouldHaveSingleItem();
        success[0].Value.ShouldBe(1);
        failure.ShouldBeEmpty();
    }

    [Fact]
    public void RecordPaymentOutcome_Failure_EmitsFailureCounter()
    {
        var success = Collect(
            "flights.payment.success_total",
            () => _sut.RecordPaymentOutcome(false)
        );
        var failure = Collect(
            "flights.payment.failure_total",
            () => _sut.RecordPaymentOutcome(false)
        );

        failure.ShouldHaveSingleItem();
        failure[0].Value.ShouldBe(1);
        success.ShouldBeEmpty();
    }

    [Fact]
    public void RecordWebhookReceived_EmitsCounter_WithEventTypeTag()
    {
        var measurements = Collect(
            "flights.webhook.received_total",
            () => _sut.RecordWebhookReceived("order.created")
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(1);
        measurements[0]
            .Tags.ShouldContain(t => t.Key == "event_type" && (string?)t.Value == "order.created");
    }

    [Fact]
    public void RecordWebhookProcessingLag_EmitsMeasurement_WithEventTypeTag()
    {
        var measurements = Collect(
            "flights.webhook.processing_lag_ms",
            () => _sut.RecordWebhookProcessingLag(250.0, "order.created")
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(250.0);
        measurements[0]
            .Tags.ShouldContain(t => t.Key == "event_type" && (string?)t.Value == "order.created");
    }

    [Fact]
    public void RecordAggregateEventsAppended_EmitsCounter_WithEventTypeTag()
    {
        var measurements = Collect(
            "flights.aggregate.events_appended_total",
            () => _sut.RecordAggregateEventsAppended("OrderConfirmed", 1)
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(1);
        measurements[0]
            .Tags.ShouldContain(t => t.Key == "event_type" && (string?)t.Value == "OrderConfirmed");
    }

    [Fact]
    public void RecordNlSearchUsage_EmitsTokensAndCostCounters()
    {
        var tokens = Collect(
            "flights.nl_search.tokens_used",
            () => _sut.RecordNlSearchUsage(100, 50, 0.005m)
        );
        var cost = Collect(
            "flights.nl_search.cost_usd",
            () => _sut.RecordNlSearchUsage(100, 50, 0.005m)
        );

        tokens.ShouldHaveSingleItem();
        tokens[0].Value.ShouldBe(150); // 100 input + 50 output

        cost.ShouldHaveSingleItem();
        cost[0].Value.ShouldBe(0.005, tolerance: 0.0001);
    }
}

/// <summary>Minimal IMeterFactory shim for unit testing without a full DI container.</summary>
internal sealed class MeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = new();

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
