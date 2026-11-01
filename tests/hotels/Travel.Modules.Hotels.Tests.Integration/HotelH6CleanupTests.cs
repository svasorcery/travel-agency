using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6CleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trace_failure_or_cancellation_does_not_skip_Host_cleanup(bool cancelled)
    {
        var host = 0;
        await Should.ThrowAsync<AggregateException>(() =>
            HotelsH6Cleanup.RunAsync(
                _ =>
                    throw (
                        cancelled ? (Exception)new OperationCanceledException() : new IOException()
                    ),
                token =>
                {
                    token.IsCancellationRequested.ShouldBeFalse();
                    host++;
                    return Task.CompletedTask;
                },
                CancellationToken.None
            )
        );
        host.ShouldBe(1);
    }

    [Fact]
    public async Task Both_errors_are_retained()
    {
        var error = await Should.ThrowAsync<AggregateException>(() =>
            HotelsH6Cleanup.RunAsync(
                _ => throw new IOException(),
                _ => throw new InvalidOperationException(),
                CancellationToken.None
            )
        );
        error.InnerExceptions.Count.ShouldBe(2);
    }
}
