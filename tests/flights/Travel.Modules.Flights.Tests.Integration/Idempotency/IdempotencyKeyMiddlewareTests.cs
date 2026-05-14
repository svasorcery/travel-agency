using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Idempotency;

[Trait("Category", "Integration")]
public sealed class IdempotencyKeyMiddlewareTests : IntegrationTestBase
{
    private static readonly Guid KnownUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private IHost _host = default!;
    private HttpClient _client = default!;
    private FlightsDbContext _db = default!;

    protected override async ValueTask OnInitializedAsync()
    {
        var opts = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new FlightsDbContext(opts);
        await _db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(TimeProvider.System);
                        services.AddScoped<FlightsDbContext>(_ =>
                        {
                            var innerOpts = new DbContextOptionsBuilder<FlightsDbContext>()
                                .UseNpgsql(ConnectionString)
                                .UseSnakeCaseNamingConvention()
                                .Options;
                            return new FlightsDbContext(innerOpts);
                        });
                        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
                    })
                    .Configure(app =>
                    {
                        // Inject fake ClaimsPrincipal with known sub before the middleware under test
                        app.Use(
                            (ctx, next) =>
                            {
                                ctx.User = new ClaimsPrincipal(
                                    new ClaimsIdentity(
                                        [new Claim("sub", KnownUserId.ToString())],
                                        "test"
                                    )
                                );
                                return next(ctx);
                            }
                        );

                        app.UseMiddleware<IdempotencyKeyMiddleware>();

                        // Terminal handler: echo 200 with a simple JSON body
                        app.Run(async ctx =>
                        {
                            ctx.Response.StatusCode = StatusCodes.Status200OK;
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync("""{"result":"ok"}""");
                        });
                    });
            })
            .StartAsync(TestContext.Current.CancellationToken);

        _client = _host.GetTestClient();
    }

    protected override async ValueTask OnDisposingAsync()
    {
        await _db.DisposeAsync();
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Post_targeted_route_with_idempotency_key_returns_200_and_persists_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var idempotencyKey = Guid.NewGuid().ToString();
        using var request = BuildRequest(idempotencyKey, """{"offerId":"abc"}""");

        using var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Idempotency-Replay").ShouldBeFalse();
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldBe("""{"result":"ok"}""");
    }

    [Fact]
    public async Task Post_same_key_same_body_returns_replay_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var idempotencyKey = Guid.NewGuid().ToString();
        const string payload = """{"offerId":"replay-test"}""";

        // First request — stores the response
        using var first = BuildRequest(idempotencyKey, payload);
        using var firstResponse = await _client.SendAsync(first, ct);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Second request — same key, same body → replay
        using var second = BuildRequest(idempotencyKey, payload);
        using var secondResponse = await _client.SendAsync(second, ct);

        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondResponse.Headers.TryGetValues("Idempotency-Replay", out var values).ShouldBeTrue();
        values!.Single().ShouldBe("true");
        var body = await secondResponse.Content.ReadAsStringAsync(ct);
        body.ShouldBe("""{"result":"ok"}""");
    }

    [Fact]
    public async Task Post_same_key_different_body_returns_409_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyGuid = Guid.NewGuid();
        var route = "/api/flights/orders/hold";

        // Seed an "in-flight" record (ResponseHash=null) with body hash "original-hash"
        // to simulate a concurrent request that is still processing.
        _db.IdempotencyKeys.Add(
            new IdempotencyKeyEntity
            {
                Key = keyGuid.ToString("N"),
                UserId = KnownUserId,
                Route = route,
                BodyHash = "original-hash",
                ResponseHash = null,
                ResponseStatus = 0,
                ResponseBody = null,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            }
        );
        await _db.SaveChangesAsync(ct);

        // Request with a different body hash — CheckOrConflict should return 409
        using var request = BuildRequest(keyGuid.ToString(), """{"offerId":"different"}""");
        using var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_without_idempotency_key_returns_400_with_error_code()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold")
        {
            Content = new StringContent("""{"offerId":"abc"}""", Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("code").GetString().ShouldBe("Flights.IdempotencyKey.Missing");
    }

    [Fact]
    public async Task Post_to_non_targeted_route_passes_through_without_requiring_key()
    {
        var ct = TestContext.Current.CancellationToken;
        // No Idempotency-Key header intentionally
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search")
        {
            Content = new StringContent("""{"origin":"JFK"}""", Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request, ct);

        // Terminal handler responds 200 — middleware should not block
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static HttpRequestMessage BuildRequest(string idempotencyKey, string jsonBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
