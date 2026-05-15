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
    public void RecordAirlineInitiatedChange_EmitsCounter()
    {
        var measurements = Collect(
            "flights.webhook.airline_change_total",
            () => _sut.RecordAirlineInitiatedChange()
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(1);
    }

    [Fact]
    public void RecordNlSearchUsage_EmitsTokensAndCostCounters()
    {
        // Two token measurements (direction-tagged) + one cost measurement
        var tokens = Collect(
            "flights.nl_search.tokens_used",
            () => _sut.RecordNlSearchUsage(100, 50, 0.005m)
        );
        var cost = Collect(
            "flights.nl_search.cost_usd",
            () => _sut.RecordNlSearchUsage(100, 50, 0.005m)
        );

        tokens.Count.ShouldBe(2); // input + output — each tagged separately

        cost.ShouldHaveSingleItem();
        cost[0].Value.ShouldBe(0.005, tolerance: 0.0001);
    }

    [Fact]
    public void Payment_duration_histogram_is_emitted()
    {
        var measurements = Collect(
            "flights.payment.duration_ms",
            () => _sut.RecordPaymentDuration(250.5, "success")
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(250.5);
        measurements[0]
            .Tags.ShouldContain(t => t.Key == "outcome" && (string?)t.Value == "success");
    }

    [Fact]
    public void Nl_search_duration_histogram_is_emitted()
    {
        var measurements = Collect(
            "flights.nl_search.duration_ms",
            () => _sut.RecordNlSearchDuration(1234.0)
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(1234.0);
    }

    [Fact]
    public void Partial_fill_rate_gauge_reflects_recent_searches()
    {
        // Record 10 searches: 4 partial failures, 6 ok
        for (var i = 0; i < 6; i++)
            _sut.RecordSearchPartialFill(partial: false);
        for (var i = 0; i < 4; i++)
            _sut.RecordSearchPartialFill(partial: true);

        var measurements = Collect(
            "flights.search.partial_fill_rate",
            () => { /* ObservableGauge — triggered by RecordObservableInstruments in Collect */
            }
        );

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(0.4, tolerance: 0.001);
    }

    [Fact]
    public void Payment_success_rate_gauge_reflects_recent_outcomes()
    {
        // 3 successes, 2 failures → 0.6
        _sut.RecordPaymentOutcome(true);
        _sut.RecordPaymentOutcome(true);
        _sut.RecordPaymentOutcome(true);
        _sut.RecordPaymentOutcome(false);
        _sut.RecordPaymentOutcome(false);

        var measurements = Collect("flights.payment.success_rate", () => { });

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(0.6, tolerance: 0.001);
    }

    [Fact]
    public void Conversion_gauge_reflects_offers_and_bookings()
    {
        // 5 offers shown, 2 booked → 0.4
        for (var i = 0; i < 5; i++)
            _sut.RecordOfferShown();
        _sut.RecordOrderBooked();
        _sut.RecordOrderBooked();

        var measurements = Collect("flights.offer_to_book_conversion", () => { });

        measurements.ShouldHaveSingleItem();
        measurements[0].Value.ShouldBe(0.4, tolerance: 0.001);
    }

    [Fact]
    public void Nl_search_tokens_are_tagged_by_direction()
    {
        var measurements = Collect(
            "flights.nl_search.tokens_used",
            () => _sut.RecordNlSearchUsage(100, 50, 0.005m)
        );

        measurements.Count.ShouldBe(2);

        var input = measurements.FirstOrDefault(m =>
            m.Tags.Any(t => t.Key == "direction" && (string?)t.Value == "input")
        );
        var output = measurements.FirstOrDefault(m =>
            m.Tags.Any(t => t.Key == "direction" && (string?)t.Value == "output")
        );

        input.Value.ShouldBe(100);
        output.Value.ShouldBe(50);
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
