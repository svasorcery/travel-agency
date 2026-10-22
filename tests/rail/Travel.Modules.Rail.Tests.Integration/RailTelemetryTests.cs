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

[Collection("RailTelemetry")]
public sealed class RailTelemetryTests
{
    private sealed class DetailRoute(string outcome) : IRailJourneyRouteProvider
    {
        public readonly TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public async Task<RailRouteResult> RouteAsync(
            Travel.Modules.Rail.Core.ScheduledJourney journey,
            RailRouteLocator locator,
            RailProfile profile,
            CancellationToken cancellationToken
        )
        {
            Started.TrySetResult();
            if (outcome == "error")
                throw new IOException(
                    "DETAILS-EXCEPTION-CANARY " + journey.Key + journey.Departure
                );
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("cancel")]
    public async Task Detail_endpoint_emitted_logs_spans_and_errors_never_contain_selection_or_body(
        string outcome
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        var capture = new Capture();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(capture);
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
                ["Rail:Enabled"] = "true",
                ["Rail:Fictional"] = "true",
                ["Rail:JourneyDetailsEnabled"] = "true",
            }
        );
        builder.AddServiceDefaults();
        var spans = new Spans();
        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(spans));
        builder.AddRailModule();
        var route = new DetailRoute(outcome);
        if (outcome != "success")
            builder.Services.AddSingleton<IRailJourneyRouteProvider>(route);
        await using var app = builder.Build();
        app.UseRailResponsePolicy();
        app.MapPost(
            "/api/rail/journey-details",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailJourneyDetailsService service,
                CancellationToken token
            ) => DetailBridge(ctx, service, token)
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer DETAILS-AUTH-CANARY");
        var reference = (
            await app
                .Services.GetRequiredService<RailReferenceService>()
                .SuggestAsync("Учебная", TestContext.Current.CancellationToken)
        ).Value;
        var query = new Travel.Modules.Rail.Core.RailRequest(
            Travel.Modules.Rail.Core.StationRef.Create("yandex-rasp", "s1").Value,
            Travel.Modules.Rail.Core.StationRef.Create("yandex-rasp", "s2").Value,
            reference.CatalogueGeneration!,
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            "all"
        );
        var snapshot = (
            await app
                .Services.GetRequiredService<RailSearchService>()
                .SearchAsync(query, TestContext.Current.CancellationToken)
        ).Value;
        var selected = snapshot.Items[0];
        var input = new RailJourneyDetailsRequest(
            query,
            snapshot.ProfileEpoch,
            new(selected.Key, selected.Transport, selected.Departure, selected.Arrival)
        );
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var pending = client.PostAsync(
            "/api/rail/journey-details",
            new StringContent(
                System.Text.Json.JsonSerializer.Serialize(
                    input,
                    System.Text.Json.JsonSerializerOptions.Web
                ),
                System.Text.Encoding.UTF8,
                "application/json"
            ),
            caller.Token
        );
        if (outcome == "cancel")
        {
            await route.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            caller.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        }
        else
        {
            using var response = await pending;
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        await spans.DetailEnded.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        var emitted = string.Join('\n', capture.Events) + string.Join('\n', spans.Values);
        foreach (
            var secret in new[]
            {
                selected.Key,
                selected.ThreadReference,
                query.DepartureDate,
                selected.Departure.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
                "DETAILS-AUTH-CANARY",
                "DETAILS-EXCEPTION-CANARY",
                "\"from\":",
                "\"to\":",
            }
        )
            emitted.ShouldNotContain(secret);
        emitted.ShouldContain("/api/rail/journey-details");
        spans.Values.ShouldNotBeEmpty();
    }

    private static Task<Microsoft.AspNetCore.Http.IResult> DetailBridge(
        Microsoft.AspNetCore.Http.HttpContext ctx,
        RailJourneyDetailsService service,
        CancellationToken token
    ) => RailEndpoints.JourneyDetails(ctx, service, token);

    private sealed class Spans : BaseProcessor<Activity>
    {
        public readonly List<string> Values = [];
        public readonly TaskCompletionSource DetailEnded = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public override void OnEnd(Activity data)
        {
            lock (Values)
            {
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
                if (Values[^1].Contains("/api/rail/journey-details", StringComparison.Ordinal))
                    DetailEnded.TrySetResult();
            }
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
