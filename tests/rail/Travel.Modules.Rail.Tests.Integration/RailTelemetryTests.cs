using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;
using Travel.Modules.Rail.Api.Composition;
using Travel.Modules.Rail.Api.Endpoints;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class RailTelemetryTests
{
    private sealed class Spans : BaseProcessor<Activity>
    {
        public readonly List<string> Values = [];

        public override void OnEnd(Activity data)
        {
            lock (Values)
                Values.Add(
                    data.DisplayName
                        + " "
                        + string.Join(";", data.TagObjects.Select(x => x.Key + "=" + x.Value))
                        + " "
                        + string.Join(
                            ";",
                            data.Events.Select(x =>
                                x.Name + string.Join(";", x.Tags.Select(y => y.Key + "=" + y.Value))
                            )
                        )
                );
        }
    }

    private sealed class Terminal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
            );
    }

    private sealed class Capture : ILoggerProvider
    {
        public readonly List<string> Events = [];

        public ILogger CreateLogger(string category) => new Logger(this, category);

        public void Dispose() { }

        private sealed class Logger(Capture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel level) => true;

            public void Log<TState>(
                LogLevel level,
                EventId id,
                TState state,
                Exception? error,
                Func<TState, Exception?, string> formatter
            )
            {
                lock (owner.Events)
                    owner.Events.Add(
                        category + " " + formatter(state, error) + " " + state + " " + error
                    );
            }
        }
    }

    [Fact]
    public async Task Actual_information_debug_hosting_events_redact_Rail_query_and_preserve_control()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var capture = new Capture();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(capture);
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { { "OTEL_EXPORTER_OTLP_ENDPOINT", null } }
        );
        builder.AddServiceDefaults();
        var spans = new Spans();
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddProcessor(spans));
        builder.AddRailModule();
        await using var app = builder.Build();
        app.MapGet(
            "/api/rail/stations",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailReferenceService service,
                CancellationToken token
            ) => Bridge(ctx, service, token)
        );
        app.MapGet("/control", () => "control");
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();
        await client.GetAsync(
            "/api/rail/stations?q=RAIL-QUERY-CANARY",
            TestContext.Current.CancellationToken
        );
        await client.GetAsync(
            "/control?ordinary=CONTROL-CANARY",
            TestContext.Current.CancellationToken
        );
        var events = string.Join('\n', capture.Events);
        events.ShouldNotContain("RAIL-QUERY-CANARY");
        events.ShouldContain("CONTROL-CANARY");
        events.ShouldContain("/api/rail/stations");
        spans.Values.ShouldNotBeEmpty();
        string.Join('\n', spans.Values).ShouldNotContain("RAIL-QUERY-CANARY");
    }

    [Fact]
    public async Task Owned_named_client_has_no_logical_or_client_handler_route_logs_without_inbound_context()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        var capture = new Capture();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(capture);
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                { "Rail:Enabled", "true" },
                { "Rail:Fictional", "true" },
            }
        );
        builder.AddRailModule();
        builder
            .Services.AddHttpClient("rail-yandex")
            .ConfigurePrimaryHttpMessageHandler(() => new Terminal());
        builder
            .Services.AddHttpClient("rail-control")
            .ConfigurePrimaryHttpMessageHandler(() => new Terminal());
        await using var app = builder.Build();
        var runtime = app.Services.GetRequiredService<RailRuntime>();
        var client = app.Services.GetRequiredService<YandexRailClient>();
        var result = await client.GetAsync(
            "search",
            "&from=RAIL-FROM-CANARY&to=RAIL-TO-CANARY&date=RAIL-DATE-CANARY",
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Dispose();
        using var control = await app
            .Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("rail-control")
            .GetAsync(
                "https://api.rasp.yandex-net.ru/CONTROL-OUTBOUND?visible=kept",
                TestContext.Current.CancellationToken
            );
        var events = string.Join('\n', capture.Events);
        events.ShouldNotContain("RAIL-FROM-CANARY");
        events.ShouldNotContain("RAIL-TO-CANARY");
        events.ShouldNotContain("RAIL-DATE-CANARY");
        events.ShouldNotContain("fictional-no-key");
        events.ShouldContain("CONTROL-OUTBOUND");
        events.ShouldContain("rail-control.LogicalHandler");
        events.ShouldContain("rail-control.ClientHandler");
        events.ShouldNotContain("rail-yandex.LogicalHandler");
        events.ShouldNotContain("rail-yandex.ClientHandler");
    }

    private static Task<Microsoft.AspNetCore.Http.IResult> Bridge(
        Microsoft.AspNetCore.Http.HttpContext context,
        RailReferenceService service,
        CancellationToken ct
    ) => RailEndpoints.Stations(context, service, ct);
}
