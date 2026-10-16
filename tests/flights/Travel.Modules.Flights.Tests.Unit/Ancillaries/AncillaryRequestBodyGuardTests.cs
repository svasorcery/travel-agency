using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Api.Middleware;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class AncillaryRequestBodyGuardTests
{
    [Theory]
    [InlineData("/api/flights/orders/quote", false)]
    [InlineData("/API/FLIGHTS/ORDERS/QUOTE", false)]
    [InlineData("/Api/Flights/Orders/Quote/", false)]
    [InlineData("/api/flights/orders/ancillaries", false)]
    [InlineData("/API/FLIGHTS/ORDERS/ANCILLARIES/", false)]
    [InlineData("/api/flights/orders/quote", true)]
    [InlineData("/API/FLIGHTS/ORDERS/QUOTE/", true)]
    [InlineData("/Api/Flights/Orders/Ancillaries/", true)]
    public async Task Oversized_known_or_chunked_body_is_rejected_before_downstream(
        string path,
        bool chunked
    )
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = "POST";
        http.Request.Path = path;
        var bytes = Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 17 * 1024) + "\"}");
        await using var body = new ChunkStream(bytes);
        http.Request.Body = body;
        http.Request.ContentLength = chunked ? null : bytes.Length;
        if (chunked)
            http.Request.Headers.TransferEncoding = "chunked";
        http.Response.Body = new MemoryStream();
        var downstream = 0;
        await new AncillaryRequestBodyGuard(_ =>
        {
            downstream++;
            return Task.CompletedTask;
        }).InvokeAsync(http);
        http.Response.StatusCode.ShouldBe(413);
        downstream.ShouldBe(0);
        http.Request.Body.ShouldBeSameAs(body);
    }

    [Theory]
    [InlineData("/API/FLIGHTS/ORDERS/QUOTE/")]
    [InlineData("/Api/Flights/Orders/Ancillaries")]
    public async Task Exact_limit_chunked_body_is_preserved_for_binding(string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = path;
        var bytes = Encoding.UTF8.GetBytes(new string('x', 16 * 1024));
        await using var body = new ChunkStream(bytes);
        http.Request.Body = body;
        var downstream = 0;
        await new AncillaryRequestBodyGuard(async context =>
        {
            downstream++;
            using var copy = new MemoryStream();
            await context.Request.Body.CopyToAsync(copy, TestContext.Current.CancellationToken);
            copy.ToArray().ShouldBe(bytes);
        }).InvokeAsync(http);
        downstream.ShouldBe(1);
        http.Request.Body.ShouldBeSameAs(body);
    }

    private sealed class ChunkStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 23)], cancellationToken);
    }
}
