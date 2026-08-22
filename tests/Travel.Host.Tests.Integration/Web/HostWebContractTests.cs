using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Travel.Host.Tests.Integration.Flights;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Shared.TestInfrastructure;
using VerifyTests;
using Wolverine;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Host.Tests.Integration.Web;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class HostWebContractTests : IntegrationTestBase
{
    private readonly FakeOrderSseRegistry _sseRegistry = new();
    private HostWebFactory _factory = default!;
    private HttpClient _client = default!;

    protected override ValueTask OnInitializedAsync()
    {
        _factory = new HostWebFactory(ConnectionString, _sseRegistry);
        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDisposingAsync()
    {
        _client.Dispose();
        if (_factory.Services.GetService<IWolverineRuntime>() is WolverineRuntime runtime)
            runtime.StopMode = StopMode.Quick;
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Development_openapi_is_anonymous_and_snapshots_real_Host_document()
    {
        using var response = await _client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken
        );
        var document = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );
        var normalized = NormalizeOpenApi(document);
        using var json = JsonDocument.Parse(normalized);
        var paths = json.RootElement.GetProperty("paths");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        paths.TryGetProperty("/api/flights/search", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/flights/orders/hold", out _).ShouldBeTrue();
        paths.TryGetProperty("/webhooks/duffel", out _).ShouldBeTrue();

        var settings = new VerifySettings();
        settings.UseDirectory("Verified");
        settings.UseFileName("HostWebContractTests.OpenApi");
        await Verifier.Verify(normalized, settings);
    }

    [Fact]
    public async Task Anonymous_authorized_endpoint_returns_problem_and_preserves_challenge()
    {
        using var response = await _client.GetAsync(
            "/api/flights/orders",
            TestContext.Current.CancellationToken
        );

        await AssertEmptyStatusProblemAsync(response, HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(challenge => challenge.Scheme == "Bearer");
    }

    [Fact]
    public async Task Authenticated_booking_without_scope_returns_forbidden_problem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(
            new { aggregateId = Guid.NewGuid(), passengers = Array.Empty<object>() }
        );

        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        await AssertEmptyStatusProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_missing_Sse_order_returns_not_found_problem()
    {
        _sseRegistry.Reset();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/events/flights/orders/{Guid.NewGuid()}"
        );
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());

        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        await AssertEmptyStatusProblemAsync(response, HttpStatusCode.NotFound);
        _sseRegistry.LookupCount.ShouldBe(1);
    }

    [Fact]
    public async Task Existing_expected_problem_body_is_not_overwritten()
    {
        using var response = await _client.PostAsJsonAsync(
            "/webhooks/duffel",
            new { id = "unsigned" },
            TestContext.Current.CancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem
            .GetProperty("type")
            .GetString()
            .ShouldBe("https://travel.local/errors/Flights.Webhook.InvalidSignature");
        problem.GetProperty("detail").GetString().ShouldBe("Webhook signature is invalid.");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .ShouldBe("Flights.Webhook.InvalidSignature");
    }

    [Fact]
    public async Task Public_health_concealment_remains_empty_not_found()
    {
        using var response = await _client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken
        );
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType.ShouldBeNull();
        body.ShouldBeEmpty();
    }

    private static async Task AssertEmptyStatusProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus
    )
    {
        response.StatusCode.ShouldBe(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;

        problem.GetProperty("status").GetInt32().ShouldBe((int)expectedStatus);
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static string NormalizeOpenApi(string document)
    {
        var root = JsonNode.Parse(document)!;
        if (root["servers"] is JsonArray servers)
        {
            foreach (var server in servers.OfType<JsonObject>())
                server["url"] = "<server>";
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class HostWebFactory(string connectionString, FakeOrderSseRegistry sseRegistry)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:travel", connectionString);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { }
                    );
                services.RemoveAll<IOrderSseRegistry>();
                services.AddSingleton<IOrderSseRegistry>(sseRegistry);
            });
        }
    }
}
