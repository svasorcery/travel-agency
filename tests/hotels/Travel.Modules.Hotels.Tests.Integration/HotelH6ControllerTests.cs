using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6ControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Captures_A_before_P1_B_before_P2_then_one_post_sample(bool contaminated)
    {
        var port = new HotelH5PurchaseReplayTests.ReplayPort(
            HotelH5PurchaseReplayTests.Saved(),
            true
        );
        var hooks = new Hooks(port, contaminated);
        var result = await HotelH5PurchaseReplayTests.Drive(port, hooks);
        result.AcquisitionComplete.ShouldBeTrue();
        result.IpcSamples.ShouldBe(36);
        result.H6Comparison!.Comparable.ShouldBe(!contaminated);
        hooks.Comparisons.ShouldBe(1);
        port.Calls.ShouldBe(new[] { "P0", "A", "P1", "B", "P2", "cleanup" });
        result.Ledger.Counts["Response"].ShouldBe(56);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Failed_snapshot_stops_next_maps_and_always_cleans(string fail)
    {
        var port = new HotelH5PurchaseReplayTests.ReplayPort(
            HotelH5PurchaseReplayTests.Saved(),
            true
        );
        var hooks = new Hooks(port, false) { Fail = fail };
        var result = await HotelH5PurchaseReplayTests.Drive(port, hooks);
        result.AcquisitionComplete.ShouldBeFalse();
        result.CleanupComplete.ShouldBeTrue();
        result.Maps.ShouldNotContain(fail == "A" ? "P1" : "P2");
        hooks.Comparisons.ShouldBe(0);
        port.Calls.Count(c => c == fail).ShouldBe(1);
    }

    [Fact]
    public async Task Active_t12_never_triggers_B()
    {
        var port = new HotelH5PurchaseReplayTests.ReplayPort(
            HotelH5PurchaseReplayTests.Saved(),
            false
        );
        var result = await HotelH5PurchaseReplayTests.Drive(port, new Hooks(port, false));
        result.AcquisitionComplete.ShouldBeFalse();
        port.Calls.ShouldContain("A");
        port.Calls.ShouldNotContain("B");
        result.H6Comparison.ShouldBeNull();
    }

    private sealed class Hooks(HotelH5PurchaseReplayTests.ReplayPort port, bool contaminated)
        : IHotelsH6Hooks
    {
        public string? Fail { get; init; }
        public int Comparisons { get; private set; }

        public Task BeforeMapAsync(string name, CancellationToken ct)
        {
            var snapshot =
                name == "P1" ? "A"
                : name == "P2" ? "B"
                : throw new InvalidOperationException();
            port.Calls.Add(snapshot);
            if (Fail == snapshot)
                throw new IOException();
            return Task.CompletedTask;
        }

        public HotelH6Comparison Compare(JsonElement before, JsonElement after)
        {
            Comparisons++;
            before.GetRawText().ShouldBe(after.GetRawText());
            return contaminated
                ? new(false, ["modeled-background-change"])
                : HotelsH6Comparison.Compare(before, after);
        }
    }
}
