using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Idempotency;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

// Direct middleware tests: no application Host, database, provider or schema startup.
public sealed class BookingRequestBodyLimitTests
{
    private const int Limit = 16 * 1024;

    [Theory]
    [InlineData("/api/flights/orders/hold", true)]
    [InlineData("/api/flights/orders/hold", false)]
    [InlineData("/API/FLIGHTS/ORDERS/HOLD/", false)]
    [InlineData("/api/flights/orders/confirm/", false)]
    [InlineData("/api/flights/orders/11111111-1111-1111-1111-111111111111/cancel/", false)]
    public async Task Oversize_is_rejected_before_store_or_handler_without_unbounded_read(
        string path,
        bool declared
    )
    {
        using var services = Services();
        using var source = new NonSeekableBody([
            .. Encoding.UTF8.GetBytes(new string('Ж', Limit / 2)),
            (byte)'x',
        ]);
        source.Bytes.Length.ShouldBe(Limit + 1);
        var context = Context(services, source, path);
        context.Request.ContentLength = declared ? source.Bytes.Length : null;
        var store = DispatchProxy.Create<IIdempotencyStore, RecordingStore>();
        var calls = (RecordingStore)(object)store;
        var handled = false;
        var middleware = new IdempotencyKeyMiddleware(_ =>
        {
            handled = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context, store);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        handled.ShouldBeFalse();
        calls.Methods.ShouldBeEmpty();
        source.BytesRead.ShouldBeLessThanOrEqualTo(declared ? 0 : Limit + 1);
        context.Request.Body.ShouldBeSameAs(source);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(
            TestContext.Current.CancellationToken
        );
        body.ShouldNotContain("Ж");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_limit_preserves_raw_bytes_hash_route_and_original_stream_lifetime(
        bool declared
    )
    {
        using var services = Services();
        var bytes = Encoding.UTF8.GetBytes(new string('Ж', Limit / 2));
        using var source = new NonSeekableBody(bytes);
        const string path = "/API/FLIGHTS/ORDERS/HOLD/";
        var context = Context(services, source, path);
        context.Request.ContentLength = declared ? bytes.Length : null;
        var store = DispatchProxy.Create<IIdempotencyStore, RecordingStore>();
        var calls = (RecordingStore)(object)store;
        var handled = false;
        var middleware = new IdempotencyKeyMiddleware(async ctx =>
        {
            handled = true;
            ctx.Request.Body.CanSeek.ShouldBeTrue();
            ctx.Request.Body.Position.ShouldBe(0);
            using var captured = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(captured, ctx.RequestAborted);
            captured.ToArray().ShouldBe(bytes);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        await middleware.InvokeAsync(context, store);
        handled.ShouldBeTrue();
        calls.Methods.ShouldBe(new[] { "TryBeginAsync", "CompleteAsync" });
        calls.Route.ShouldBe(path);
        var prefix = Encoding.UTF8.GetBytes($"POST\n{path}\n");
        calls.BodyHash.ShouldBe(Convert.ToHexString(SHA256.HashData([.. prefix, .. bytes])));
        context.Request.Body.ShouldBeSameAs(source);
        source.WasDisposed.ShouldBeFalse();
        source.BytesRead.ShouldBe(bytes.Length);
    }

    [Fact]
    public async Task Missing_key_on_route_alias_stops_before_reading_the_body()
    {
        using var services = Services();
        using var source = new NonSeekableBody([1, 2, 3]);
        var context = Context(services, source, "/api/flights/orders/hold/");
        context.Request.Headers.Remove("Idempotency-Key");
        var store = DispatchProxy.Create<IIdempotencyStore, RecordingStore>();
        var handled = false;
        await new IdempotencyKeyMiddleware(_ =>
        {
            handled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, store);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        handled.ShouldBeFalse();
        ((RecordingStore)(object)store).Methods.ShouldBeEmpty();
        source.BytesRead.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_valid_identity_stops_before_body_read_store_or_handler(
        bool authenticated
    )
    {
        using var services = Services();
        using var source = new NonSeekableBody([1, 2, 3]);
        var context = Context(services, source, "/API/FLIGHTS/ORDERS/HOLD/");
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                authenticated ? [new Claim("sub", "invalid-owner")] : [],
                authenticated ? "test" : null
            )
        );
        var store = DispatchProxy.Create<IIdempotencyStore, RecordingStore>();
        var handled = false;
        await new IdempotencyKeyMiddleware(_ =>
        {
            handled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, store);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        handled.ShouldBeFalse();
        ((RecordingStore)(object)store).Methods.ShouldBeEmpty();
        source.BytesRead.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_alias_replay_preserves_no_store_without_calling_handler()
    {
        using var services = Services();
        using var source = new NonSeekableBody(Encoding.UTF8.GetBytes("{}"));
        var context = Context(
            services,
            source,
            "/API/FLIGHTS/ORDERS/11111111-1111-1111-1111-111111111111/CANCEL/"
        );
        var store = DispatchProxy.Create<IIdempotencyStore, RecordingStore>();
        var calls = (RecordingStore)(object)store;
        calls.Replay = new IdempotencyRecord("hash", 200, "{}");
        var handled = false;
        await new IdempotencyKeyMiddleware(_ =>
        {
            handled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, store);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        handled.ShouldBeFalse();
        calls.Methods.ShouldBe(new[] { "TryBeginAsync" });
    }

    private static ServiceProvider Services() =>
        new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();

    private static DefaultHttpContext Context(IServiceProvider services, Stream source, string path)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = TestContext.Current.CancellationToken,
        };
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test")
        );
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.Body = source;
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        context.Response.Body = new MemoryStream();
        return context;
    }

    public class RecordingStore : DispatchProxy
    {
        public List<string> Methods { get; } = [];
        public string? BodyHash { get; private set; }
        public string? Route { get; private set; }
        public IdempotencyRecord? Replay { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!.Name;
            Methods.Add(method);
            if (method == nameof(IIdempotencyStore.TryBeginAsync))
            {
                Route = (string)args![2]!;
                BodyHash = (string)args[3]!;
                return Task.FromResult(
                    Replay is null
                        ? new BeginResult(BeginOutcome.Started, null)
                        : new BeginResult(BeginOutcome.Replay, Replay)
                );
            }
            if (
                method
                is nameof(IIdempotencyStore.CompleteAsync)
                    or nameof(IIdempotencyStore.AbandonAsync)
            )
                return Task.CompletedTask;
            throw new InvalidOperationException("Unexpected store call.");
        }
    }

    private sealed class NonSeekableBody(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public byte[] Bytes { get; } = bytes;
        public int BytesRead { get; private set; }
        public bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
