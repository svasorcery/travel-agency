using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH5ReviewTests
{
    [Fact]
    public void Failed_startup_cannot_be_reported_as_cleaned_when_no_owned_handle_was_returned()
    {
        var progress = new HotelH5Progress { StartupAttempted = true, HostAcquired = false };
        Should.Throw<InvalidDataException>(() => progress.RequireKnownCleanup());
        new HotelH5Progress().RequireKnownCleanup();
        new HotelH5Progress { StartupAttempted = true, HostAcquired = true }.RequireKnownCleanup();
    }

    [Fact]
    public async Task Identity_failure_still_hashes_and_retains_all_raw_evidence()
    {
        var integrity = false;
        bool? marker = null;
        var ok = await HotelsH5Finalization.FinishAsync(
            () => Task.CompletedTask,
            () => throw new InvalidDataException(),
            () =>
            {
                integrity = true;
                return Task.CompletedTask;
            },
            (complete, errors) =>
            {
                marker = complete;
                return Task.CompletedTask;
            },
            () => true
        );
        ok.ShouldBeFalse();
        marker.ShouldBe(false);
        integrity.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_or_deadline_during_marker_write_corrects_success_to_failure()
    {
        var eligible = true;
        var writes = new List<bool>();
        var ok = await HotelsH5Finalization.FinishAsync(
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            (complete, errors) =>
            {
                writes.Add(complete);
                eligible = false;
                return Task.CompletedTask;
            },
            () => eligible
        );
        ok.ShouldBeFalse();
        writes.ShouldBe(new[] { true, false });
    }

    [Fact]
    public async Task Receipt_failure_does_not_skip_integrity_or_failure_marker()
    {
        var integrity = false;
        bool? marker = null;
        var ok = await HotelsH5Finalization.FinishAsync(
            () => throw new IOException(),
            () => Task.CompletedTask,
            () =>
            {
                integrity = true;
                return Task.CompletedTask;
            },
            (complete, errors) =>
            {
                marker = complete;
                return Task.CompletedTask;
            },
            () => true
        );
        ok.ShouldBeFalse();
        marker.ShouldBe(false);
        integrity.ShouldBeTrue();
    }
}
