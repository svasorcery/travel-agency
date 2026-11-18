using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripFlightLinksDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exporters_and_framework_log_scopes_exclude_private_link_data(bool enabled)
    {
        var audit = new Audit();
        var slow = false;
        using var tracer = new SpanSink(audit);
        using var meters = new MetricSink(audit);
        var reader = new PeriodicExportingMetricReader(meters, 60000);
        await using var host = new TripsHostFixture(
            flightLinks: enabled,
            diagnostics: services =>
            {
                services.AddLogging(l =>
                    l.SetMinimumLevel(LogLevel.Information).AddProvider(audit)
                );
                services.AddSingleton<ITripsReadiness>(s => new TimedReadiness(
                    ActivatorUtilities.CreateInstance<TripsReadiness>(s),
                    () => slow
                ));
                services
                    .AddOpenTelemetry()
                    .WithTracing(t => t.AddProcessor(new SimpleActivityExportProcessor(tracer)))
                    .WithMetrics(m => m.AddReader(reader));
            }
        );
        var ct = TestContext.Current.CancellationToken;
        await host.StartAsync(ct);
        using var created = await host.Client.PostAsJsonAsync(
            "/api/trips",
            new
            {
                title = "Diagnostic controls",
                brief = new
                {
                    destinationId = "moscow",
                    originId = (string?)null,
                    startDate = "2026-11-18",
                    endDate = "2026-11-19",
                    adults = 1,
                    currency = "RUB",
                    totalBudget = (string?)null,
                    interests = new[] { "architecture" },
                    pace = "balanced",
                },
            },
            ct
        );
        created.EnsureSuccessStatusCode();
        var trip = created.Headers.Location!.OriginalString.Split('/')[^1];
        var order = Guid.NewGuid().ToString();
        const string token = "fictional-link-trace-marker";
        string path = "/API/TRIPS/" + trip + "/FLIGHT-LINKS/";
        audit.Values.Clear();
        using var control = await host.Client.GetAsync("/openapi/v1.json", ct);
        control.EnsureSuccessStatusCode();
        using var availability = await host.Client.GetAsync(
            "/api/trips/flight-links/availability",
            ct
        );
        availability.EnsureSuccessStatusCode();
        using var missing = await host.Client.GetAsync(path, ct);
        ((int)missing.StatusCode).ShouldBe(enabled ? 200 : 404);
        var absentTrip = Guid.NewGuid().ToString();
        using var absent = await host.Client.GetAsync(path.Replace(trip, absentTrip), ct);
        ((int)absent.StatusCode).ShouldBe(404);
        using var invalid = await host.Client.GetAsync(path + "?unexpected=" + token, ct);
        ((int)invalid.StatusCode).ShouldBe(enabled ? 400 : 404);
        using var change = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(new { op = "remove", orderId = order }),
        };
        change.Headers.TryAddWithoutValidation("If-Match", "\"fl-0\"");
        change.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var patch = await host.Client.SendAsync(change, ct);
        ((int)patch.StatusCode).ShouldBe(enabled ? 200 : 404);
        if (enabled)
        {
            slow = true;
            using var timed = await host.Client.GetAsync(path, ct);
            ((int)timed.StatusCode).ShouldBe(504);
        }
        const string railQuery = "fictional-rail-export-marker";
        using var rail = await host.Client.GetAsync("/api/rail/stations?q=" + railQuery, ct);
        host.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var anon = await host.Client.GetAsync(path, ct);
        ((int)anon.StatusCode).ShouldBe(enabled ? 401 : 404);
        reader.Collect(5000).ShouldBeTrue();
        tracer.ControlRequestExported.ShouldBeTrue(
            "Expected a real server span for the public OpenAPI request."
        );
        meters.ServerRequestExported.ShouldBeTrue("Expected an ASP.NET HTTP request metric.");
        audit.LogCount.ShouldBeGreaterThan(0);
        audit
            .Values.Any(value => value.Contains("/openapi", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue();
        foreach (var secret in new[] { trip, absentTrip, order, token, railQuery })
            audit
                .Values.Any(v => v.Contains(secret, StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse();
    }

    private sealed class TimedReadiness(ITripsReadiness normal, Func<bool> slow) : ITripsReadiness
    {
        public async Task<bool> IsReadyAsync(CancellationToken ct)
        {
            if (slow())
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return await normal.IsReadyAsync(ct);
        }
    }

    private sealed class SpanSink(Audit audit) : BaseExporter<Activity>
    {
        internal bool ControlRequestExported;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var item in batch)
            {
                if (
                    item.Kind == ActivityKind.Server
                    && item.TagObjects.Any(tag =>
                        tag.Key is "http.route" or "url.path"
                        && tag.Value?.ToString()
                            ?.Contains("/openapi", StringComparison.OrdinalIgnoreCase) == true
                    )
                )
                    ControlRequestExported = true;
                audit.Values.Enqueue(item.DisplayName);
                foreach (var pair in item.TagObjects)
                    audit.Values.Enqueue(pair.ToString());
                foreach (var itemEvent in item.Events)
                {
                    audit.Values.Enqueue(itemEvent.Name);
                    foreach (var pair in itemEvent.Tags)
                        audit.Values.Enqueue(pair.ToString());
                }
            }
            return ExportResult.Success;
        }
    }

    private sealed class MetricSink(Audit audit) : BaseExporter<Metric>
    {
        internal bool ServerRequestExported;

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                if (metric.Name == "http.server.request.duration")
                    ServerRequestExported = true;
                foreach (ref readonly var point in metric.GetMetricPoints())
                foreach (var tag in point.Tags)
                    audit.Values.Enqueue(tag.ToString());
            }
            return ExportResult.Success;
        }
    }

    private sealed class Audit : ILoggerProvider, ILogger
    {
        internal ConcurrentQueue<string> Values { get; } = new();
        internal int LogCount;

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose() { }

        public bool IsEnabled(LogLevel level) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            Capture(state);
            return null;
        }

        public void Log<TState>(
            LogLevel level,
            EventId id,
            TState state,
            Exception? ex,
            Func<TState, Exception?, string> formatter
        )
        {
            Interlocked.Increment(ref LogCount);
            Values.Enqueue(formatter(state, ex));
            Capture(state);
        }

        private void Capture<TState>(TState state)
        {
            Values.Enqueue(state?.ToString() ?? "");
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                foreach (var pair in pairs)
                    Values.Enqueue(pair.ToString());
        }
    }
}
