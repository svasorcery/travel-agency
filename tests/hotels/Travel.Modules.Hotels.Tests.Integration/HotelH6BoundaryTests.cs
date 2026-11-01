using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6BoundaryTests
{
    [Theory]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("parent")]
    [InlineData("sid")]
    [InlineData("elevation")]
    [InlineData("image")]
    [InlineData("arguments")]
    public void Target_tuple_rejects_reuse_wrong_owner_role_or_command(string change)
    {
        var image = Path.GetFullPath("dotnet.exe");
        var value = new HotelH6Process(
            42,
            100,
            7,
            image,
            "owner",
            false,
            new[] { image, "wrapper.dll", "m2-host", "profile.json" }
        );
        value = change switch
        {
            "pid" => value with { Pid = 43 },
            "start" => value with { StartUtcTicks = 101 },
            "parent" => value with { ParentPid = 8 },
            "sid" => value with { UserSid = "other" },
            "elevation" => value with { Elevated = true },
            "image" => value with { Image = Path.GetFullPath("other.exe") },
            "arguments" => value with
            {
                Arguments = new[] { image, "other.dll", "m2-host", "profile.json" },
            },
            _ => value,
        };
        Should.Throw<InvalidDataException>(() =>
            HotelsH6Windows.Validate(
                value,
                42,
                100,
                7,
                "owner",
                false,
                image,
                ["wrapper.dll", "m2-host", "profile.json"]
            )
        );
    }

    [Fact]
    public void Owned_tuple_is_accepted_without_logging_command_line()
    {
        var image = Path.GetFullPath("dotnet.exe");
        var value = new HotelH6Process(
            42,
            100,
            7,
            image,
            "owner",
            false,
            [image, "wrapper.dll", "m2-host", "profile.json"]
        );
        HotelsH6Windows.Validate(
            value,
            42,
            100,
            7,
            "owner",
            false,
            image,
            ["wrapper.dll", "m2-host", "profile.json"]
        );
    }

    [Fact]
    public void Native_metadata_reads_only_this_test_process()
    {
        if (!OperatingSystem.IsWindows())
        {
            Should.Throw<PlatformNotSupportedException>(() =>
                HotelsH6Windows.ReadProcess(Environment.ProcessId)
            );
            return;
        }
        using var process = Process.GetCurrentProcess();
        var value = HotelsH6Windows.ReadProcess(process.Id);
        value.Pid.ShouldBe(process.Id);
        value.StartUtcTicks.ShouldBe(process.StartTime.ToUniversalTime().Ticks);
        value.ParentPid.ShouldBeGreaterThan(0);
        value.Image.ShouldBe(process.MainModule!.FileName);
        value.UserSid.ShouldBe(WindowsIdentity.GetCurrent().User!.Value);
        value.Arguments.Length.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4097)]
    public async Task Pipe_frame_bounds_reject_before_json(int count)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        using var stream = new MemoryStream(bytes);
        await Should.ThrowAsync<InvalidDataException>(() =>
            HotelsH6Wire.ReadAsync<HotelH6Message>(stream, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Pipe_frame_roundtrip_and_unknown_fields_do_not_accept_arbitrary_commands()
    {
        using var stream = new MemoryStream();
        await HotelsH6Wire.WriteAsync(
            stream,
            new HotelH6Message(1, "bind", new(42, 100)),
            TestContext.Current.CancellationToken
        );
        stream.Position = 0;
        var value = await HotelsH6Wire.ReadAsync<HotelH6Message>(
            stream,
            TestContext.Current.CancellationToken
        );
        value.ShouldBe(new HotelH6Message(1, "bind", new(42, 100)));
        using var bad = new MemoryStream();
        await HotelsH6Wire.WriteAsync(
            bad,
            new
            {
                sequence = 1,
                operation = "bind",
                executable = "arbitrary.exe",
            },
            TestContext.Current.CancellationToken
        );
        bad.Position = 0;
        await Should.ThrowAsync<JsonException>(() =>
            HotelsH6Wire.ReadAsync<HotelH6Message>(bad, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Pipe_peer_pids_are_checked_with_current_process_only_and_duplicate_name_is_refused()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows-only transport, policy tests run independently.
        var name = "TravelHotelsH6-Test-" + Guid.NewGuid().ToString("N");
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        using var server = HotelsH6Wire.Server(name, sid);
        using var client = HotelsH6Wire.Client(name);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        limit.CancelAfter(TimeSpan.FromSeconds(3));
        var waiting = server.WaitForConnectionAsync(limit.Token);
        await client.ConnectAsync(limit.Token);
        await waiting;
        HotelsH6Windows.ServerPid(client).ShouldBe(Environment.ProcessId);
        HotelsH6Windows.ClientPid(server).ShouldBe(Environment.ProcessId);
        Should.Throw<Exception>(() => HotelsH6Wire.Server(name, sid));
    }
}
