using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6ReviewRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_ETL_never_creates_projected_over_budget_output(bool shrinks)
    {
        var dir = Path.Combine(Path.GetTempPath(), "h6-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "source");
        var destination = Path.Combine(dir, "copy");
        File.WriteAllBytes(source, new byte[2 * 1024 * 1024]);
        var checks = 0;
        long Written() => File.Exists(destination) ? new FileInfo(destination).Length : 0;
        long Remaining() =>
            shrinks
                ? Written() == 0
                    ? 3 * 1024 * 1024
                    : 512 * 1024
                : 1024 * 1024;
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                HotelsH6Copy.FileAsync(
                    source,
                    destination,
                    Remaining,
                    () => checks++,
                    TestContext.Current.CancellationToken
                )
            );
            if (shrinks)
            {
                Written().ShouldBe(1024 * 1024);
                checks.ShouldBeGreaterThan(1);
            }
            else
                File.Exists(destination).ShouldBeFalse();
            new FileInfo(source).Length.ShouldBe(2 * 1024 * 1024);
        }
        finally
        {
            File.Delete(source);
            File.Delete(destination);
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task Copy_checks_resources_during_blocks_and_honors_cancellation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "h6-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "source");
        var destination = Path.Combine(dir, "copy");
        File.WriteAllBytes(source, new byte[3 * 1024 * 1024]);
        using var stop = new CancellationTokenSource();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() =>
                HotelsH6Copy.FileAsync(
                    source,
                    destination,
                    () => 10 * 1024 * 1024,
                    () =>
                    {
                        if (
                            File.Exists(destination)
                            && new FileInfo(destination).Length >= 1024 * 1024
                        )
                            stop.Cancel();
                    },
                    stop.Token
                )
            );
            new FileInfo(destination).Length.ShouldBe(1024 * 1024);
        }
        finally
        {
            File.Delete(source);
            File.Delete(destination);
            Directory.Delete(dir);
        }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("stop")]
    [InlineData("cancel")]
    public async Task Optional_observer_failure_still_executes_cleanup_command(string operation)
    {
        var calls = new List<string>();
        var result = await HotelsH6CommandObservation.ExecuteAsync(
            true,
            () => throw new IOException(),
            before =>
            {
                before.Value.ShouldBeNull();
                before.Error.ShouldBe(nameof(IOException));
                calls.Add(operation);
                return Task.FromResult(0);
            }
        );
        result.ShouldBe(0);
        calls.ShouldBe(new[] { operation });
    }

    [Fact]
    public async Task Measurement_observer_failure_still_stops_measurement()
    {
        var called = false;
        await Should.ThrowAsync<IOException>(() =>
            HotelsH6CommandObservation.ExecuteAsync(
                false,
                () => throw new IOException(),
                _ =>
                {
                    called = true;
                    return Task.FromResult(0);
                }
            )
        );
        called.ShouldBeFalse();
    }
}
