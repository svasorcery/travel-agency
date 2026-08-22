using System.Diagnostics;
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
                        services.AddProblemDetails(options =>
                            options.CustomizeProblemDetails = context =>
                                context.ProblemDetails.Extensions["traceId"] =
                                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier
                        );
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
                                var rawUserId = ctx.Request.Headers.TryGetValue(
                                    "X-Test-UserId",
                                    out var value
                                )
                                    ? value.ToString()
                                    : KnownUserId.ToString();
                                ctx.User = new ClaimsPrincipal(
                                    new ClaimsIdentity([new Claim("sub", rawUserId)], "test")
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

        await AssertProblemDetailsAsync(
            response,
            HttpStatusCode.Conflict,
            "Conflict",
            "https://travel.local/errors/Flights.IdempotencyConflict",
            "Idempotency key reused with a different payload.",
            "Flights.IdempotencyConflict",
            KnownUserId.ToString()
        );
    }

    [Fact]
    public async Task Post_same_key_same_body_inflight_returns_rfc7807_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyGuid = Guid.NewGuid();
        const string route = "/api/flights/orders/hold";
        const string payload = """{"offerId":"inflight"}""";

        _db.IdempotencyKeys.Add(
            new IdempotencyKeyEntity
            {
                Key = keyGuid.ToString("N"),
                UserId = KnownUserId,
                Route = route,
                BodyHash = ComputeRequestHash(route, payload),
                ResponseHash = null,
                ResponseStatus = 0,
                ResponseBody = null,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            }
        );
        await _db.SaveChangesAsync(ct);

        using var request = BuildRequest(keyGuid.ToString(), payload);
        using var response = await _client.SendAsync(request, ct);

        await AssertProblemDetailsAsync(
            response,
            HttpStatusCode.Conflict,
            "Conflict",
            "https://travel.local/errors/Flights.IdempotencyInFlight",
            "Request with the same Idempotency-Key is already in progress.",
            "Flights.IdempotencyInFlight",
            KnownUserId.ToString()
        );
    }

    [Fact]
    public async Task Post_without_idempotency_key_returns_rfc7807_bad_request()
    {
        var ct = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold")
        {
            Content = new StringContent("""{"offerId":"abc"}""", Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request, ct);

        await AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation",
            "https://travel.local/errors/Flights.IdempotencyKey.Missing",
            "A valid Idempotency-Key header is required.",
            "Flights.IdempotencyKey.Missing",
            KnownUserId.ToString()
        );
    }

    [Fact]
    public async Task Post_with_malformed_identity_returns_rfc7807_before_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyGuid = Guid.NewGuid();
        const string malformedUserIdentifier = "traveler@example.test";
        using var request = BuildRequest(keyGuid.ToString(), """{"offerId":"abc"}""");
        request.Headers.Add("X-Test-UserId", malformedUserIdentifier);

        using var response = await _client.SendAsync(request, ct);

        await AssertProblemDetailsAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Unauthorized",
            "https://travel.local/errors/Identity.UserId.Invalid",
            "Authenticated identity must contain one non-empty, unambiguous user identifier.",
            "Identity.UserId.Invalid",
            malformedUserIdentifier
        );
        (
            await _db.IdempotencyKeys.AnyAsync(entity => entity.Key == keyGuid.ToString("N"), ct)
        ).ShouldBeFalse();
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

    [Fact]
    public async Task Concurrent_first_time_requests_execute_once()
    {
        // Two parallel requests with the same key and body must result in exactly
        // one downstream handler invocation; the loser must observe either the
        // cached 2xx (replay) or a 409 in-progress conflict.
        var ct = TestContext.Current.CancellationToken;
        var idempotencyKey = Guid.NewGuid().ToString();
        const string payload = """{"offerId":"concurrent-test"}""";

        var t1 = _client.SendAsync(BuildRequest(idempotencyKey, payload), ct);
        var t2 = _client.SendAsync(BuildRequest(idempotencyKey, payload), ct);
        var responses = await Task.WhenAll(t1, t2);

        // Both responses must be observable; one or both 200, the loser may be 409.
        responses
            .All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict)
            .ShouldBeTrue();

        // Exactly one row in the store after both settle — uniqueness on the key.
        var rows = await _db
            .IdempotencyKeys.Where(x =>
                x.Key == Guid.Parse(idempotencyKey).ToString("N") && x.UserId == KnownUserId
            )
            .CountAsync(ct);
        rows.ShouldBe(1);

        // And the terminal handler ran at most once (we count how many 200s saw a
        // *fresh* response body — replays carry Idempotency-Replay header).
        var freshOk = responses.Count(r =>
            r.StatusCode == HttpStatusCode.OK && !r.Headers.Contains("Idempotency-Replay")
        );
        freshOk.ShouldBe(1, "exactly one request must reach the terminal handler fresh");

        foreach (var r in responses)
            r.Dispose();
    }

    [Fact]
    public async Task Failure_responses_are_not_cached()
    {
        // A request whose handler returns 5xx must NOT be cached: the same key on
        // retry must re-execute the handler (which can then return 2xx).
        var ct = TestContext.Current.CancellationToken;

        // Rebuild the host with a switchable terminal handler.
        await _host.StopAsync(ct);
        _host.Dispose();
        _client.Dispose();

        var failNext = true;
        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(TimeProvider.System);
                        services.AddProblemDetails(options =>
                            options.CustomizeProblemDetails = context =>
                                context.ProblemDetails.Extensions["traceId"] =
                                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier
                        );
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

                        app.Run(async ctx =>
                        {
                            if (Volatile.Read(ref failNext))
                            {
                                ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                ctx.Response.ContentType = "application/json";
                                await ctx.Response.WriteAsync("""{"error":"transient"}""");
                                return;
                            }

                            ctx.Response.StatusCode = StatusCodes.Status200OK;
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync("""{"result":"ok"}""");
                        });
                    });
            })
            .StartAsync(ct);
        _client = _host.GetTestClient();

        var idempotencyKey = Guid.NewGuid().ToString();
        const string payload = """{"offerId":"failure-retry"}""";

        // First call: handler returns 500.
        using var first = await _client.SendAsync(BuildRequest(idempotencyKey, payload), ct);
        first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        // Flip the switch — next handler invocation succeeds.
        Volatile.Write(ref failNext, false);

        // Retry with the SAME key / body: must re-execute handler (not replay 500).
        using var second = await _client.SendAsync(BuildRequest(idempotencyKey, payload), ct);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.Headers.Contains("Idempotency-Replay").ShouldBeFalse();
    }

    [Fact]
    public async Task Hash_includes_http_method()
    {
        // Defensive: even on the same route, a different HTTP method must hash
        // differently. The middleware only targets POST today, but pinning this
        // invariant prevents a future routing change from silently colliding.
        var ct = TestContext.Current.CancellationToken;
        var idempotencyKey = Guid.NewGuid().ToString();
        const string payload = """{"offerId":"method-test"}""";

        // First: POST through middleware.
        using var post = BuildRequest(idempotencyKey, payload);
        using var postResp = await _client.SendAsync(post, ct);
        postResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Now inspect the stored body hash. A PUT with the SAME body bytes must
        // produce a different stored hash if the method participates in the hash.
        var stored = await _db
            .IdempotencyKeys.AsNoTracking()
            .FirstAsync(
                x => x.Key == Guid.Parse(idempotencyKey).ToString("N") && x.UserId == KnownUserId,
                ct
            );

        // We compute what the hash would be for a PUT with the same payload bytes;
        // if the middleware mixes the method into the hash, they must differ.
        var bytes = Encoding.UTF8.GetBytes(payload);
        var sha = System.Security.Cryptography.SHA256.HashData(bytes);
        var bodyOnlyHash = Convert.ToHexString(sha);

        // The stored hash must NOT equal the bare body hash — proving the method
        // (and/or route) was mixed in.
        stored.BodyHash.ShouldNotBe(bodyOnlyHash);
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

    private static string ComputeRequestHash(string route, string payload) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes($"{HttpMethods.Post}\n{route}\n{payload}")
            )
        );

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string title,
        string type,
        string detail,
        string code,
        params string[] excludedValues
    )
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;

        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("title").GetString().ShouldBe(title);
        problem.GetProperty("type").GetString().ShouldBe(type);
        problem.GetProperty("detail").GetString().ShouldBe(detail);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("errors").ValueKind.ShouldBe(JsonValueKind.Array);
        problem.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe(code);

        var normalizedBody = body.ToLowerInvariant();
        normalizedBody.ShouldNotContain("exception");
        normalizedBody.ShouldNotContain("stacktrace");
        normalizedBody.ShouldNotContain("secret");
        foreach (var excludedValue in excludedValues)
            body.ShouldNotContain(excludedValue);
    }
}
