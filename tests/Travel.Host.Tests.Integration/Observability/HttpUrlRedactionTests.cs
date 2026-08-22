using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Shared.Infrastructure.Telemetry;
using Xunit;

namespace Travel.Host.Tests.Integration.Observability;

[Trait("Category", "Integration")]
public sealed class HttpUrlRedactionTests
{
    private const string TokenSecret = "travelpayouts-super-secret";
    private const string AdditionalSecret = "second-provider-super-secret";

    [Fact]
    public async Task Registered_http_instrumentation_redacts_all_contributed_query_keys()
    {
        await using var server = new SingleResponseLoopbackServer();
        var exporter = new CapturingActivityExporter();
        var builder = BuildHostGraph();
        builder.Services.AddSingleton<IHttpUrlRedactionContributor>(
            new TestUrlRedactionContributor("api_key")
        );
        builder
            .Services.AddOpenTelemetry()
            .WithTracing(tracing =>
                tracing.AddProcessor(new SimpleActivityExportProcessor(exporter))
            );
        builder.Services.AddHttpClient("redaction-observed");
        using var services = builder.Services.BuildServiceProvider();
        _ = services.GetRequiredService<TracerProvider>();
        var client = services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("redaction-observed");
        var ct = TestContext.Current.CancellationToken;
        var responseTask = server.RespondOnceAsync(ct);
        var requestUri = new Uri(
            server.BaseAddress,
            $"prices?ToKeN={TokenSecret}&visible=kept-value&API_KEY={AdditionalSecret}"
        );

        using var response = await client.GetAsync(requestUri, ct);
        await responseTask;
        var activity = exporter.Activities.Single(activity =>
            activity.UrlFull.Contains("/prices?", StringComparison.Ordinal)
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        activity.UrlFull.ShouldContain("ToKeN=REDACTED");
        activity.UrlFull.ShouldContain("API_KEY=REDACTED");
        activity.UrlFull.ShouldContain("visible=kept-value");
        activity.AllTags.ShouldNotContain(TokenSecret);
        activity.AllTags.ShouldNotContain(AdditionalSecret);
    }

    private static HostApplicationBuilder BuildHostGraph()
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Development,
                ApplicationName = "Travel.Host.Tests.Integration",
            }
        );
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.Configuration["ConnectionStrings:travel"] =
            "Host=localhost;Database=travel;Username=test;Password=test";
        builder.Configuration["ConnectionStrings:redis"] = "localhost:6379";
        builder.Configuration["Flights:FeatureFlags:Travelpayouts:Enabled"] = "true";
        builder.Configuration["Flights:Duffel:BaseUrl"] = "https://duffel.example";
        builder.Configuration["Flights:Duffel:ApiKey"] = "duffel-test-key";
        builder.Configuration["Flights:Duffel:WebhookSecret"] = "duffel-test-webhook-secret";
        builder.Configuration["Flights:Travelpayouts:BaseUrl"] = "https://travelpayouts.example";
        builder.Configuration["Flights:Travelpayouts:ApiToken"] = "travelpayouts-test-token";
        builder.Configuration["Flights:Travelpayouts:PartnerMarker"] = "travelpayouts-test-marker";
        builder.Configuration["Flights:Providers:Frankfurter:BaseAddress"] =
            "https://frankfurter.example/";
        builder.Configuration["Flights:Smtp:Host"] = "localhost";
        builder.Configuration["Flights:Smtp:Port"] = "1025";
        builder.Configuration["Flights:Smtp:FromAddress"] = "noreply@travel.example";
        builder.AddServiceDefaults();
        builder.AddFlightsModule();
        return builder;
    }

    private sealed class TestUrlRedactionContributor(params string[] names)
        : IHttpUrlRedactionContributor
    {
        public IReadOnlyCollection<string> SensitiveQueryParameterNames { get; } = names;
    }

    private sealed class CapturingActivityExporter : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<CapturedActivity> _activities = new();

        public IReadOnlyCollection<CapturedActivity> Activities => [.. _activities];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                var urlFull = activity.GetTagItem("url.full")?.ToString();
                if (urlFull is null)
                    continue;

                _activities.Enqueue(
                    new CapturedActivity(
                        urlFull,
                        string.Join(
                            "\n",
                            activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")
                        )
                    )
                );
            }

            return ExportResult.Success;
        }
    }

    private sealed record CapturedActivity(string UrlFull, string AllTags);

    private sealed class SingleResponseLoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public SingleResponseLoopbackServer()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseAddress = new Uri($"http://127.0.0.1:{endpoint.Port}/");
        }

        public Uri BaseAddress { get; }

        public async Task RespondOnceAsync(CancellationToken ct)
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            var buffer = new byte[4096];
            var received = new StringBuilder();
            while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0)
                    break;
                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
            );
            await stream.WriteAsync(response, ct);
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
