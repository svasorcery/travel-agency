using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Rail.Api.Endpoints;
using Travel.Modules.Rail.Api.Middleware;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Infrastructure;
using Travel.ServiceDefaults.Web;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class RailHttpTests
{
    [Fact]
    public async Task Platform_exception_response_preserves_Rail_no_store_and_safe_body()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails(PlatformProblemDetails.Configure);
        builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
        await using var app = builder.Build();
        app.UsePlatformWebDefaults();
        app.UseMiddleware<RailResponsePolicy>();
        app.MapGet("/api/rail/failure", ThrowFailure);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(
            "/api/rail/failure",
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("500");
        body.ShouldNotContain("fictional-failure-canary");
        body.ShouldNotContain(nameof(InvalidOperationException));
    }

    private static Microsoft.AspNetCore.Http.IResult ThrowFailure() =>
        throw new InvalidOperationException("fictional-failure-canary");

    [Fact]
    public async Task Real_routing_distinguishes_no_store_skipped_invalid_and_oversized_without_Host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<RailReferenceService>(_ =>
            new(new Disabled(), new NeverLoader(), TimeProvider.System)
        );
        builder.Services.AddSingleton<RailSearchService>(_ =>
            new(
                new Disabled(),
                new(new Disabled(), new NeverLoader(), TimeProvider.System),
                new NeverProvider(),
                new RailSearchCache(TimeProvider.System),
                TimeProvider.System
            )
        );
        await using var app = builder.Build();
        app.MapGet(
            "/api/rail/stations",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailReferenceService service,
                CancellationToken token
            ) => StationBridge(ctx, service, token)
        );
        app.MapPost(
            "/api/rail/search",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailSearchService service,
                CancellationToken token
            ) => SearchBridge(ctx, service, token)
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        var client = app.GetTestClient();
        var station = await client.GetAsync(
            "/api/rail/stations?q=fictional",
            TestContext.Current.CancellationToken
        );
        station.StatusCode.ShouldBe(HttpStatusCode.OK);
        station.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (
            await station.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldContain("skipped");
        var invalid = await client.PostAsync(
            "/api/rail/search",
            new StringContent(
                "{\"secretUnknown\":\"canary-value\"}",
                Encoding.UTF8,
                "application/json"
            ),
            TestContext.Current.CancellationToken
        );
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (
            await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain("canary-value");
        var oversized = await client.PostAsync(
            "/api/rail/search",
            new StringContent(new string('x', 8193), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        oversized.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private static Task<Microsoft.AspNetCore.Http.IResult> StationBridge(
        Microsoft.AspNetCore.Http.HttpContext ctx,
        RailReferenceService service,
        CancellationToken token
    ) => RailEndpoints.Stations(ctx, service, token);

    private static Task<Microsoft.AspNetCore.Http.IResult> SearchBridge(
        Microsoft.AspNetCore.Http.HttpContext ctx,
        RailSearchService service,
        CancellationToken token
    ) => RailEndpoints.Search(ctx, service, token);

    private sealed class Disabled : IRailRuntimeProfile
    {
        public RailProfile Current => new("off", "real", false, default);
    }

    private sealed class NeverLoader : IRailReferenceLoader
    {
        public Task<ErrorOr.ErrorOr<ReferenceData>> LoadAsync(
            RailProfile profile,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Forbidden loader");
    }

    private sealed class NeverProvider : IRailScheduleProvider
    {
        public Task<ModeResult> SearchAsync(
            Travel.Modules.Rail.Core.RailRequest request,
            string mode,
            RailProfile profile,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Forbidden provider");
    }
}
