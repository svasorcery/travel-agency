using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Api.Middleware;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class SavedTravelersMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reader_is_capped_at_16_KiB_plus_one_and_restores_original_stream(
        bool knownLength
    )
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();
        var stream = new CountingStream(new byte[20 * 1024]);
        var context = Context(services, stream);
        if (knownLength)
            context.Request.ContentLength = 20 * 1024;
        var effects = 0;
        var guard = new SavedTravelerBodyGuardMiddleware(_ =>
        {
            effects++;
            return Task.CompletedTask;
        });
        await guard.InvokeAsync(
            context,
            Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions())
        );
        context.Response.StatusCode.ShouldBe(413);
        stream.BytesRead.ShouldBe(knownLength ? 0 : 16 * 1024 + 1);
        context.Request.Body.ShouldBeSameAs(stream);
        effects.ShouldBe(0);
    }

    [Fact]
    public async Task Reader_only_uses_memory_and_restores_original_stream_even_when_next_throws()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();
        var stream = new CountingStream(Encoding.UTF8.GetBytes("{}"));
        var context = Context(services, stream);
        var guard = new SavedTravelerBodyGuardMiddleware(http =>
        {
            http.Request.Body.ShouldBeOfType<MemoryStream>();
            http.Request.Body.Position.ShouldBe(0);
            throw new InvalidOperationException("controlled test");
        });
        await Should.ThrowAsync<InvalidOperationException>(() =>
            guard.InvokeAsync(
                context,
                Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions())
            )
        );
        context.Request.Body.ShouldBeSameAs(stream);
    }

    [Theory]
    [InlineData("/api/flights/travelers/missing", 404, true)]
    [InlineData("/API/FLIGHTS/TRAVELERS/", 404, true)]
    [InlineData("/api/flights/travelers/throws", 500, true)]
    [InlineData("/api/flights/travelers-evil", 404, false)]
    public async Task Response_policy_is_segment_scoped_and_survives_exception_response(
        string path,
        int status,
        bool noStore
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.Use(
            async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (InvalidOperationException)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("controlled", context.RequestAborted);
                }
            }
        );
        app.UseFlightsResponsePolicy();
        app.MapGet("/api/flights/travelers/throws", (HttpContext _) => ThrowControlled());
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var response = await app.GetTestClient()
            .GetAsync(path, TestContext.Current.CancellationToken);
        ((int)response.StatusCode).ShouldBe(status);
        if (noStore)
            (response.Headers.CacheControl?.NoStore).ShouldBe(true);
        else
            response.Headers.CacheControl.ShouldBeNull();
    }

    private static IResult ThrowControlled() => throw new InvalidOperationException("controlled");

    private static DefaultHttpContext Context(IServiceProvider services, Stream body)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test")
            ),
        };
        context.Request.Method = "PUT";
        context.Request.Path = "/api/flights/travelers/11111111-1111-1111-1111-111111111111";
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }
}
