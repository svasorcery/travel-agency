using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6ProtocolTests
{
    [Fact]
    public async Task Real_wire_roundtrip_runs_only_fixed_sequence_and_releases_after_hashed_handoff()
    {
        var sys = new HotelH6PolicyTests.Fake();
        var trace = new HotelsH6Trace(sys, Guid.NewGuid().ToString("N"), Path.GetTempPath());
        var saved = false;
        var released = false;
        var protocol = new HotelsH6Protocol(
            trace,
            receipt =>
            {
                receipt.Snapshots.ShouldBe(2);
                saved = true;
                return Task.FromResult(new string('A', 64));
            },
            hash =>
            {
                saved.ShouldBeTrue();
                hash.ShouldBe(new string('A', 64));
                released = true;
            }
        );
        async Task<HotelH6Reply> Send(HotelH6Message message)
        {
            using var frame = new MemoryStream();
            await HotelsH6Wire.WriteAsync(frame, message, CancellationToken.None);
            frame.Position = 0;
            return await protocol.ApplyAsync(
                await HotelsH6Wire.ReadAsync<HotelH6Message>(frame, CancellationToken.None),
                CancellationToken.None
            );
        }
        (await Send(new(1, "attach", new(42, 100)))).Ok.ShouldBeTrue();
        (await Send(new(2, "A"))).Ok.ShouldBeTrue();
        (await Send(new(3, "B"))).Ok.ShouldBeTrue();
        var result = await Send(new(4, "finish"));
        result
            .Data!.Value.Deserialize<HotelH6Handoff>(HotelsH6Wire.Json)!
            .Trace.EtlSaved.ShouldBeTrue();
        released.ShouldBeFalse();
        (await Send(new(5, "release", Fingerprint: new string('A', 64)))).Ok.ShouldBeTrue();
        released.ShouldBeTrue();
        (await Send(new(6, "A"))).Ok.ShouldBeFalse();
        sys.Calls.Select(c => c.Op)
            .ShouldBe(new[] { "enable", "start", "A", "B", "disable", "stop" });
    }

    [Theory]
    [InlineData("bad-sequence")]
    [InlineData("arbitrary-command")]
    [InlineData("target-on-A")]
    [InlineData("early-release")]
    [InlineData("fingerprint-on-attach")]
    public async Task Invalid_request_is_terminal_but_cleanup_still_works(string kind)
    {
        var sys = new HotelH6PolicyTests.Fake();
        var protocol = new HotelsH6Protocol(
            new(sys, Guid.NewGuid().ToString("N"), Path.GetTempPath()),
            _ => Task.FromResult(new string('B', 64)),
            _ => throw new Exception("Must not release")
        );
        var bad = kind switch
        {
            "bad-sequence" => new HotelH6Message(9, "attach", new(42, 100)),
            "arbitrary-command" => new(1, "cancel"),
            "target-on-A" => new(1, "A", new(42, 100)),
            "early-release" => new(1, "release", Fingerprint: new string('A', 64)),
            _ => new(1, "attach", new(42, 100), new string('A', 64)),
        };
        (await protocol.ApplyAsync(bad, CancellationToken.None)).Ok.ShouldBeFalse();
        (
            await protocol.ApplyAsync(new(2, "attach", new(42, 100)), CancellationToken.None)
        ).Ok.ShouldBeFalse();
        sys.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Evidence_failure_cannot_prevent_trace_cleanup()
    {
        var sys = new HotelH6PolicyTests.Fake();
        var protocol = new HotelsH6Protocol(
            new(sys, Guid.NewGuid().ToString("N"), Path.GetTempPath()),
            _ => throw new IOException(),
            _ => { }
        );
        (
            await protocol.ApplyAsync(new(1, "attach", new(42, 100)), CancellationToken.None)
        ).Ok.ShouldBeTrue();
        (await protocol.ApplyAsync(new(2, "finish"), CancellationToken.None)).Ok.ShouldBeFalse();
        sys.Calls.Select(c => c.Op).ShouldBe(new[] { "enable", "start", "disable", "stop" });
    }
}
