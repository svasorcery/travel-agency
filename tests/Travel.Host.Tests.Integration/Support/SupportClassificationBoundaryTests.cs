using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Api.Endpoints;
using Travel.Modules.Support.Api.Middleware;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

// Direct HTTP boundary only; no Host Program, test server, issuer, DB, keys or model runtime.
public sealed class SupportClassificationBoundaryTests
{
    private const string Valid =
        "{\"requestId\":\"22222222-2222-4222-8222-222222222222\",\"expectedRevision\":1}";
    private const string Path =
        "/api/support/operator/cases/11111111-1111-4111-8111-111111111111/classification-suggestion";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Classification_body_is_bounded_to_one_KiB_before_dispatch(bool knownLength)
    {
        await using var services = Services();
        using var body = new CountingBody(new byte[40 * 1024]);
        var ctx = Context(services, body);
        if (knownLength)
            ctx.Request.ContentLength = body.Length;
        var effects = 0;
        await Invoke(
            ctx,
            _ =>
            {
                effects++;
                return Task.CompletedTask;
            }
        );
        ctx.Response.StatusCode.ShouldBe(413);
        body.ReadBytes.ShouldBe(knownLength ? 0 : 1025);
        ctx.Request.Body.ShouldBeSameAs(body);
        effects.ShouldBe(0);
        ctx.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requestId\":\"22222222-2222-4222-8222-222222222222\",\"expectedRevision\":0}")]
    [InlineData("{\"requestId\":\"22222222-2222-7222-8222-222222222222\",\"expectedRevision\":1}")]
    [InlineData(
        "{\"requestId\":\"22222222-2222-4222-8222-222222222222\",\"expectedRevision\":1,\"expectedRevision\":2}"
    )]
    [InlineData(
        "{\"requestId\":\"22222222-2222-4222-8222-222222222222\",\"expectedRevision\":1,\"text\":\"private\"}"
    )]
    [InlineData(
        "{\"requestId\":\"22222222-2222-4222-8222-222222222222\",\"expectedRevision\":\"1\"}"
    )]
    public async Task Malformed_or_extra_metadata_never_reaches_dispatch(string payload)
    {
        await using var services = Services();
        using var body = new CountingBody(Encoding.UTF8.GetBytes(payload));
        var ctx = Context(services, body);
        var effects = 0;
        await Invoke(
            ctx,
            _ =>
            {
                effects++;
                return Task.CompletedTask;
            }
        );
        ctx.Response.StatusCode.ShouldBe(400);
        effects.ShouldBe(0);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("customer", 403)]
    [InlineData("wrong-client", 403)]
    public async Task Operator_authority_precedes_body_read(string actor, int expected)
    {
        await using var services = Services();
        using var body = new CountingBody(Encoding.UTF8.GetBytes(Valid));
        var ctx = Context(services, body);
        ctx.User =
            actor == "anonymous"
                ? new ClaimsPrincipal()
                : Principal(
                    actor == "customer" ? "openid" : "support:operator",
                    actor == "wrong-client" ? "travel-web" : "travel-support-operator"
                );
        await Invoke(ctx, _ => throw new InvalidOperationException("Must not dispatch."));
        ctx.Response.StatusCode.ShouldBe(expected);
        body.ReadBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Valid_metadata_dispatches_once_and_restores_original_body()
    {
        await using var services = Services();
        using var body = new CountingBody(Encoding.UTF8.GetBytes(Valid));
        var ctx = Context(services, body);
        var effects = 0;
        await Invoke(
            ctx,
            async passed =>
            {
                effects++;
                using var json = await JsonDocument.ParseAsync(
                    passed.Request.Body,
                    cancellationToken: TestContext.Current.CancellationToken
                );
                json.RootElement.EnumerateObject().Count().ShouldBe(2);
                json.RootElement.GetProperty("expectedRevision").GetInt32().ShouldBe(1);
            }
        );
        effects.ShouldBe(1);
        ctx.Request.Body.ShouldBeSameAs(body);
    }

    [Fact]
    public async Task Endpoint_projects_only_bound_metadata_and_busy_has_no_retry_dispatch()
    {
        await using var services = Services();
        using var body = new CountingBody([]);
        var ctx = Context(services, body);
        var svc = new Classification();
        var request = new RequestSupportClassificationDto(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            1
        );
        var result = await RequestSupportClassificationEndpoint.Post(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            request,
            ctx,
            svc,
            TestContext.Current.CancellationToken
        );
        await result.ExecuteAsync(ctx);
        svc.Calls.ShouldBe(1);
        ctx.Response.StatusCode.ShouldBe(200);
        ctx.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(
            ctx.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken
        );
        json.RootElement.GetProperty("queue").GetString().ShouldBe("Refund");
        json.RootElement.GetProperty("category").GetString().ShouldBe("Refund");
        json.RootElement.GetProperty("capturedRevision").GetInt32().ShouldBe(1);
        json.RootElement.ToString().ShouldNotContain("ownerId");
        svc.Failure = SupportErrors.ClassificationBusy;
        var busy = Context(services, new MemoryStream());
        await (
            await RequestSupportClassificationEndpoint.Post(
                Guid.NewGuid(),
                request,
                busy,
                svc,
                TestContext.Current.CancellationToken
            )
        ).ExecuteAsync(busy);
        busy.Response.StatusCode.ShouldBe(429);
        busy.Response.Headers.RetryAfter.ToString().ShouldBe("60");
        svc.Calls.ShouldBe(2);
    }

    private static Task Invoke(HttpContext ctx, RequestDelegate next) =>
        new SupportResponsePolicyMiddleware(c =>
            new SupportBodyGuardMiddleware(next).InvokeAsync(c)
        ).InvokeAsync(ctx);

    private static ServiceProvider Services() =>
        new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();

    private static ClaimsPrincipal Principal(
        string scope = "support:operator",
        string azp = "travel-support-operator"
    ) =>
        new(
            new ClaimsIdentity(
                [
                    new("sub", "44444444-4444-4444-8444-444444444444"),
                    new("scope", scope),
                    new("azp", azp),
                ],
                "Test"
            )
        );

    private static DefaultHttpContext Context(IServiceProvider services, Stream body)
    {
        var ctx = new DefaultHttpContext { RequestServices = services, User = Principal() };
        ctx.Request.Method = "POST";
        ctx.Request.Path = Path;
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = body;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class CountingBody(byte[] payload) : MemoryStream(payload)
    {
        public int ReadBytes { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken ct = default
        )
        {
            var count = await base.ReadAsync(buffer, ct);
            ReadBytes += count;
            return count;
        }
    }

    private sealed class Classification : ISupportClassificationService
    {
        public int Calls { get; private set; }
        public Error? Failure { get; set; }

        public Task<ErrorOr<SupportClassificationResult>> SuggestAsync(
            SupportCaller caller,
            RequestSupportClassification request,
            CancellationToken ct = default
        )
        {
            Calls++;
            if (Failure is { } error)
                return Task.FromResult<ErrorOr<SupportClassificationResult>>(error);
            var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
            return Task.FromResult<ErrorOr<SupportClassificationResult>>(
                new SupportClassificationResult(
                    request.CaseId,
                    request.RequestId,
                    request.ExpectedRevision,
                    now,
                    now.AddMinutes(2),
                    SupportClassificationStatus.Candidate,
                    SupportClassificationReason.Ready,
                    ManualCategory.Refund,
                    new string('a', 64),
                    SupportClassificationInput.QuestionVersion,
                    new(SupportUrgencyKind.Unknown, null, null, null)
                )
            );
        }
    }
}
