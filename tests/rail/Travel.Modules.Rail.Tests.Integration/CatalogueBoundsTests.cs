using System.Text;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class CatalogueBoundsTests
{
    private sealed class Chunks(IEnumerable<string> chunks) : Stream
    {
        private readonly IEnumerator<string> chunks = chunks.GetEnumerator();
        private byte[] current = [];
        private int offset;
        public long Bytes;

        public override int Read(byte[] buffer, int start, int count) =>
            Read(buffer.AsSpan(start, count));

        public override int Read(Span<byte> buffer)
        {
            var written = 0;
            while (written < buffer.Length)
            {
                if (offset == current.Length)
                {
                    if (!chunks.MoveNext())
                        break;
                    current = Encoding.UTF8.GetBytes(chunks.Current);
                    offset = 0;
                }
                var size = Math.Min(current.Length - offset, buffer.Length - written);
                current.AsSpan(offset, size).CopyTo(buffer[written..]);
                offset += size;
                written += size;
            }
            Bytes += written;
            return written;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                chunks.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long value, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int start, int count) =>
            throw new NotSupportedException();
    }

    private static string ValidPrefix =>
        "{\"countries\":["
        + CatalogueParserTests.Country("l225", CatalogueParserTests.Station(), false)
        + "],\"extension\":";

    [Theory]
    [InlineData(16384, false)]
    [InlineData(16385, true)]
    public async Task Token_boundary_is_exercised_with_otherwise_valid_RF(int length, bool failed)
    {
        using var stream = new Chunks([ValidPrefix, "\"", new string('x', length), "\"}"]);
        var result = await YandexStationCatalogueParser.ParseAsync(
            stream,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBe(failed);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(33, true)]
    public async Task Depth_boundary_is_exercised_inside_unknown_extension(int depth, bool failed)
    {
        using var stream = new Chunks([
            ValidPrefix,
            new string('[', depth),
            "0",
            new string(']', depth),
            "}",
        ]);
        (
            await YandexStationCatalogueParser.ParseAsync(
                stream,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBe(failed);
    }

    [Fact]
    public async Task Decoded_64MiB_overflow_rejects_whole_valid_candidate()
    {
        static IEnumerable<string> Body()
        {
            yield return ValidPrefix + "[";
            for (var i = 0; i < 4200; i++)
                yield return (i > 0 ? "," : "") + "\"" + new string('x', 16000) + "\"";
            yield return "]}";
        }
        using var stream = new Chunks(Body());
        var result = await YandexStationCatalogueParser.ParseAsync(
            stream,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        stream.Bytes.ShouldBeGreaterThan(64L * 1024 * 1024);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Retained_48MiB_includes_hierarchy_and_remaining_staging(
        int excess,
        bool failed
    )
    {
        static IEnumerable<string> Body(int excess)
        {
            const int count = 100000;
            const int parentBytes = 204;
            var codeBytes = Enumerable.Range(1, count).Sum(i => (long)("s" + i).Length);
            var names = 48L * 1024 * 1024 - parentBytes - codeBytes - count * (64L + 200) + excess;
            yield return "{\"countries\":[{\"codes\":{\"yandex_code\":\"l225\"},\"regions\":[{\"title\":\""
                + new string('r', 100)
                + "\",\"settlements\":[{\"title\":\""
                + new string('t', 100)
                + "\",\"stations\":[";
            for (var i = 1; i <= count; i++)
            {
                var size = (int)(names / count + (i <= names % count ? 1 : 0));
                yield return (i > 1 ? "," : "")
                    + "{\"codes\":{\"yandex_code\":\"s"
                    + i
                    + "\"},\"title\":\""
                    + new string('x', size)
                    + "\",\"transport_type\":\"train\",\"station_type\":\"station\"}";
            }
            yield return "]}]}]}]}";
        }
        using var stream = new Chunks(Body(excess));
        var result = await YandexStationCatalogueParser.ParseAsync(
            stream,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBe(failed);
        stream.Bytes.ShouldBeLessThan(64L * 1024 * 1024);
        if (!failed)
            result.Value.PayloadBytes.ShouldBe(48L * 1024 * 1024 - 204);
    }
}
