using System.Diagnostics;
using System.Text;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH5CollectorTests
{
    [Theory]
    [InlineData("identity")]
    [InlineData("eula")]
    [InlineData("hash")]
    public async Task Failed_prelaunch_guard_starts_zero_collectors(string reason)
    {
        var starts = 0;
        await Should.ThrowAsync<InvalidDataException>(() =>
            HotelsH5Collector.CaptureAsync(
                "tool",
                "output.xml",
                42,
                () => throw new InvalidDataException(reason),
                () => { },
                _ =>
                {
                    starts++;
                    throw new Exception();
                },
                TestContext.Current.CancellationToken
            )
        );
        starts.ShouldBe(0);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("exit")]
    [InlineData("stderr")]
    [InlineData("overflow")]
    [InlineData("missing")]
    [InlineData("bad-xml")]
    [InlineData("safety")]
    public async Task Collector_fault_kills_only_its_owned_process_and_cannot_return_a_map(
        string fault
    )
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "travel-h5-collector-" + Guid.NewGuid() + ".xml"
        );
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var fake = new Fake(fault);
        if (fault == "timeout")
            cts.CancelAfter(TimeSpan.FromMilliseconds(30));
        var launched = false;
        try
        {
            await Should.ThrowAsync<Exception>(() =>
                HotelsH5Collector.CaptureAsync(
                    "tool",
                    path,
                    42,
                    () => { },
                    () =>
                    {
                        if (fault == "safety" && launched)
                            throw new InvalidDataException();
                    },
                    _ =>
                    {
                        launched = true;
                        if (fault == "bad-xml")
                            File.WriteAllText(path, "<partial");
                        return fake;
                    },
                    cts.Token,
                    new(@"C:\Windows", @"C:\ProgramData", "C:")
                )
            );
            launched.ShouldBeTrue();
            fake.Disposed.ShouldBeTrue();
            if (fault is "timeout" or "overflow" or "safety")
                fake.Killed.ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Collector_to_reader_accepts_saved_real_export_with_correct_system_paths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "travel-h5-replay-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "P0.xml");
        var launches = 0;
        try
        {
            var actual = await HotelsH5Collector.CaptureAsync(
                "unused-tool.exe",
                output,
                22504,
                () => { },
                () => { },
                info =>
                {
                    launches++;
                    info.WorkingDirectory.ShouldBe(directory);
                    info.Environment["TEMP"].ShouldBe(directory);
                    info.Environment["TMP"].ShouldBe(directory);
                    info.Environment["SystemDrive"].ShouldNotBeNullOrWhiteSpace();
                    info.Environment["ProgramData"].ShouldNotBeNullOrWhiteSpace();
                    info.ArgumentList.ShouldBe(new[] { "-p", "22504", output });
                    using var source = File.OpenRead(
                        Path.Combine(AppContext.BaseDirectory, "Fixtures", "h5-vmmap-p0.xml.gz")
                    );
                    using var gzip = new System.IO.Compression.GZipStream(
                        source,
                        System.IO.Compression.CompressionMode.Decompress
                    );
                    using var destination = File.Open(output, FileMode.CreateNew);
                    gzip.CopyTo(destination);
                    return new Fake("success"); // No Process.Start, Host, DB or VMMap.
                },
                TestContext.Current.CancellationToken,
                new(@"C:\Windows", @"C:\ProgramData", "C:")
            );
            actual.Regions.ShouldBe(2420);
            actual.ParentPrivateMismatchCount.ShouldBe(284);
            actual.WorkingSetBytes.ShouldBe(393240576);
            launches.ShouldBe(1);
        }
        finally
        {
            File.Delete(output);
            Directory.Delete(directory);
        }
    }

    private sealed class Fake(string fault) : IHotelH5CollectorProcess
    {
        public StreamReader Output { get; } =
            Reader(fault == "overflow" ? new string('x', 8193) : "partial");
        public StreamReader Error { get; } = Reader(fault == "stderr" ? "error" : "");
        public bool HasExited => Killed || fault is not ("timeout" or "overflow" or "safety");
        public int ExitCode => fault == "exit" ? 1 : 0;
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }

        public Task WaitAsync(CancellationToken ct) =>
            HasExited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, ct);

        public void Kill()
        {
            Killed = true;
        }

        public void Dispose()
        {
            Disposed = true;
            Output.Dispose();
            Error.Dispose();
        }

        private static StreamReader Reader(string value) =>
            new(new MemoryStream(Encoding.UTF8.GetBytes(value)));
    }
}
