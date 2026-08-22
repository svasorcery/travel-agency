using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.ServiceDefaults.Hosting;
using Travel.Shared.Abstractions;
using Travel.Shared.Web;
using VerifyTests;
using Xunit;

namespace Travel.Host.Tests.Integration.Web;

public sealed class PlatformWebBaselineTests
{
    private const string ExceptionSecret = "platform-exception-secret";
    private static readonly Uri HandlerUri = new("https://platform.test/failure");

    [Fact]
    public async Task Unhandled_exception_returns_safe_rfc7807_response_with_trace_id()
    {
        await using var app = BuildWebApplication();
        app.UsePlatformWebDefaults();
        app.MapGet("/throws", ThrowUnhandledException);
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient()
            .GetAsync("/throws", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(body);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        problem.RootElement.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.RootElement.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
        problem.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        body.ShouldNotContain(ExceptionSecret);
        body.ShouldNotContain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Expected_ErrorOr_endpoint_failure_uses_safe_customized_rfc7807()
    {
        const string requestSecret = "expected-error-secret";
        const string requestPii = "traveler@example.test";
        await using var app = BuildWebApplication();
        app.UsePlatformWebDefaults();
        app.MapGet(
            "/expected-error",
            () =>
                Results.Problem(
                    new List<Error>
                    {
                        Error.NotFound(
                            "Flights.Order.NotFound",
                            "The requested order was not found."
                        ),
                    }.ToProblemDetails()
                )
        );
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient()
            .GetAsync(
                $"/expected-error?secret={requestSecret}&email={requestPii}",
                TestContext.Current.CancellationToken
            );
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.GetProperty("title").GetString().ShouldBe("NotFound");
        problem
            .GetProperty("type")
            .GetString()
            .ShouldBe("https://travel.local/errors/Flights.Order.NotFound");
        problem.GetProperty("detail").GetString().ShouldBe("The requested order was not found.");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .ShouldBe("Flights.Order.NotFound");

        body.ShouldNotContain(requestSecret);
        body.ShouldNotContain(requestPii);
        body.ToLowerInvariant().ShouldNotContain("exception");
        body.ToLowerInvariant().ShouldNotContain("stacktrace");
    }

    [Fact]
    public async Task Development_openapi_is_anonymous_under_host_fallback_policy_and_matches_snapshot()
    {
        var builder = CreateWebBuilder(Environments.Development);
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build()
        );

        await using var app = builder.Build();
        app.UsePlatformWebDefaults();
        app.UseAuthorization();
        app.MapGet("/platform-baseline", () => new PlatformBaselineResponse("ready"))
            .WithName("GetPlatformBaseline");
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient()
            .GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var normalized = NormalizeOpenApi(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = new VerifySettings();
        settings.UseDirectory("Verified");
        settings.UseFileName("PlatformWebBaselineTests.OpenApi");
        await Verifier.Verify(normalized, settings);
    }

    [Fact]
    public async Task Production_does_not_map_openapi()
    {
        await using var app = BuildWebApplication(Environments.Production);
        app.UsePlatformWebDefaults();
        app.MapFallback(() => Results.NotFound()).AllowAnonymous();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient()
            .GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Default_http_client_makes_one_attempt_on_server_error()
    {
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildHttpClientServices("default", handler, static _ => { });
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("default");

        using var response = await client.GetAsync(
            HandlerUri,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        handler.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Explicit_platform_resilience_makes_four_bounded_attempts()
    {
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildHttpClientServices(
            "resilient",
            handler,
            client => client.AddPlatformHttpResilience()
        );
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("resilient");

        using var response = await client.GetAsync(
            HandlerUri,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        handler.AttemptCount.ShouldBe(4);
    }

    [Fact]
    public void Platform_time_provider_is_singleton()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.AddServiceDefaults();
        using var services = builder.Services.BuildServiceProvider();

        var first = services.GetRequiredService<TimeProvider>();
        var second = services.GetRequiredService<TimeProvider>();

        first.ShouldBeSameAs(TimeProvider.System);
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void Platform_time_provider_can_be_overridden_by_tests()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2035, 4, 3, 2, 1, 0, TimeSpan.Zero));
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.AddServiceDefaults();
        builder.Services.AddSingleton<TimeProvider>(fakeTime);
        using var services = builder.Services.BuildServiceProvider();

        services.GetRequiredService<TimeProvider>().ShouldBeSameAs(fakeTime);
    }

    [Fact]
    public void Test_only_guard_rejects_test_registration_in_production()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GuardedTestService>();
        var environment = new FakeHostEnvironment(Environments.Production);

        var exception = Should.Throw<InvalidOperationException>(() =>
            TestOnlyGuard.Verify(services, environment)
        );

        exception.Message.ShouldContain(typeof(GuardedTestService).FullName!);
    }

    [Fact]
    public void Shared_web_assembly_does_not_own_test_only_guard()
    {
        var sharedWebAssembly = typeof(Travel.Shared.Web.HttpRequestExtensions).Assembly;

        sharedWebAssembly.GetType("Travel.Shared.Web.TestOnlyGuard").ShouldBeNull();
    }

    private static WebApplication BuildWebApplication(string environment = "Development") =>
        CreateWebBuilder(environment).Build();

    private static WebApplicationBuilder CreateWebBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(PlatformWebBaselineTests).Assembly.FullName,
                EnvironmentName = environment,
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.AddServiceDefaults();
        return builder;
    }

    private static ServiceProvider BuildHttpClientServices(
        string name,
        CountingHandler handler,
        Action<IHttpClientBuilder> configure
    )
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;
        builder.AddServiceDefaults();
        var client = builder
            .Services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        configure(client);
        return builder.Services.BuildServiceProvider();
    }

    private static Task ThrowUnhandledException(HttpContext context) =>
        throw new InvalidOperationException(ExceptionSecret);

    private static string NormalizeOpenApi(string document)
    {
        var root = JsonNode.Parse(document)!;
        if (root["servers"] is JsonArray servers)
        {
            foreach (var server in servers.OfType<JsonObject>())
                server["url"] = "<server>";
        }

        NormalizeTraceIds(root);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void NormalizeTraceIds(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (string.Equals(property.Key, "traceId", StringComparison.OrdinalIgnoreCase))
                    jsonObject[property.Key] = "<traceId>";
                else if (property.Value is not null)
                    NormalizeTraceIds(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray.Where(item => item is not null))
                NormalizeTraceIds(item!);
        }
    }

    private sealed class CountingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _attemptCount);
            return Task.FromResult(
                new HttpResponseMessage(statusCode) { RequestMessage = request }
            );
        }
    }

    [TestOnly]
    private sealed class GuardedTestService;

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "PlatformWebBaselineTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed record PlatformBaselineResponse(string Status);
}
