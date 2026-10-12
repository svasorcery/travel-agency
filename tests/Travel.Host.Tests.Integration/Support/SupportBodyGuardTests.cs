using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Support.Api.Middleware;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

public sealed class SupportBodyGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounded_reader_consumes_at_most_32_KiB_plus_one_and_restores_original(
        bool knownLength
    )
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();
        using var original = new CountingStream(new byte[40 * 1024]);
        var context = Context(services, original);
        if (knownLength)
            context.Request.ContentLength = original.Length;
        var effects = 0;
        await new SupportBodyGuardMiddleware(_ =>
        {
            effects++;
            return Task.CompletedTask;
        }).InvokeAsync(context);
        context.Response.StatusCode.ShouldBe(413);
        original.ReadBytes.ShouldBe(knownLength ? 0 : SupportBodyGuardMiddleware.MaximumBytes + 1);
        context.Request.Body.ShouldBeSameAs(original);
        effects.ShouldBe(0);
    }

    [Fact]
    public async Task Reader_restores_original_on_exception_and_authorization_precedes_any_body_read()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();
        var payload = Encoding.UTF8.GetBytes(
            "{\"messageId\":\"22222222-2222-4222-8222-222222222222\",\"title\":\"Fictional\",\"body\":\"Fictional\"}"
        );
        using var original = new CountingStream(payload);
        var context = Context(services, original);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            new SupportBodyGuardMiddleware(_ =>
                throw new InvalidOperationException("Controlled fixture failure.")
            ).InvokeAsync(context)
        );
        context.Request.Body.ShouldBeSameAs(original);
        using var unauthorizedBody = new CountingStream(payload);
        var unauthorized = Context(services, unauthorizedBody);
        unauthorized.User = new ClaimsPrincipal();
        await new SupportBodyGuardMiddleware(_ =>
            throw new InvalidOperationException("Must not run.")
        ).InvokeAsync(unauthorized);
        unauthorized.Response.StatusCode.ShouldBe(401);
        unauthorizedBody.ReadBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Non_UTF8_charset_is_rejected_before_the_binder_can_decode_the_submitted_text_differently()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();
        using var original = new CountingStream(Encoding.UTF8.GetBytes("{}"));
        var context = Context(services, original);
        context.Request.ContentType = "application/json; charset=utf-16";
        await new SupportBodyGuardMiddleware(_ =>
            throw new InvalidOperationException("Must not run.")
        ).InvokeAsync(context);
        context.Response.StatusCode.ShouldBe(400);
        original.ReadBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_UTF8_is_a_fixed_error_without_echo()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddProblemDetails()
            .BuildServiceProvider();
        using var original = new CountingStream([0xff, 0xfe, 0x61]);
        var context = Context(services, original);
        await new SupportBodyGuardMiddleware(_ =>
            throw new InvalidOperationException("Must not run.")
        ).InvokeAsync(context);
        context.Response.StatusCode.ShouldBe(400);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain(
            "Support.InvalidInput"
        );
    }

    private static DefaultHttpContext Context(IServiceProvider services, Stream body)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "PUT";
        context.Request.Path = "/api/support/cases/11111111-1111-4111-8111-111111111111";
        context.Request.ContentType = "application/json";
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", SupportHttpService.Owner.ToString("D"))], "Test")
        );
        return context;
    }

    private sealed class CountingStream(byte[] payload) : MemoryStream(payload)
    {
        public int ReadBytes { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            ReadBytes += count;
            return count;
        }
    }
}
