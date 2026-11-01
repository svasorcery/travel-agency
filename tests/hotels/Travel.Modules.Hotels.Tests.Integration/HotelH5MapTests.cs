using System.Text;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH5MapTests
{
    [Theory]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("root")]
    [InlineData("binary")]
    [InlineData("manifest")]
    [InlineData("exited")]
    public void Capture_rejects_changed_owned_identity_before_launch(string field)
    {
        var owned = new HotelH5Identity(42, 12345, "root", "binary", "manifest");
        var observed = field switch
        {
            "pid" => owned with { Pid = 43 },
            "start" => owned with { StartUtcTicks = 1 },
            "root" => owned with { Root = "other" },
            "binary" => owned with { BinaryHash = "other" },
            "manifest" => owned with { ManifestHash = "other" },
            _ => owned,
        };
        Should.Throw<InvalidDataException>(() =>
            HotelsH5Map.ValidateIdentity(owned, observed, field == "exited")
        );
    }

    [Fact]
    public void Exact_owned_identity_and_hidden_numeric_pid_arguments_are_required()
    {
        var identity = new HotelH5Identity(42, 12345, "root", "binary", "manifest");
        HotelsH5Map.ValidateIdentity(identity, identity, false);
        var output = Path.Combine(Path.GetTempPath(), "P0.xml");
        var info = HotelsH5Map.StartInfo("vmmap64.exe", 42, output);
        info.CreateNoWindow.ShouldBeTrue();
        info.UseShellExecute.ShouldBeFalse();
        info.WindowStyle.ShouldBe(System.Diagnostics.ProcessWindowStyle.Hidden);
        info.ArgumentList.ShouldBe(new[] { "-p", "42", output });
        info.ArgumentList.ShouldNotContain("-accepteula");
        info.Environment.ContainsKey("_NT_SYMBOL_PATH").ShouldBeFalse();
    }

    [Fact]
    public void Commit_private_and_resident_counts_are_distinct_and_unknown_types_are_retained()
    {
        WithFile(
            Xml(),
            path =>
            {
                var map = HotelsH5Map.Read(path, 42);
                map.Pid.ShouldBe(42);
                map.Regions.ShouldBe(1);
                map.CommittedBytes.ShouldBe(8192);
                map.PrivateBytes.ShouldBe(4096);
                map.WorkingSetBytes.ShouldBe(1024);
                map.Types.ShouldContain("999");
            }
        );
    }

    [Theory]
    [InlineData("wrong-pid")]
    [InlineData("dtd")]
    [InlineData("truncated")]
    [InlineData("schema")]
    [InlineData("missing-counter")]
    [InlineData("negative")]
    [InlineData("no-regions")]
    public void Malformed_or_unsupported_xml_is_incomplete_never_zero(string fault)
    {
        var xml = fault switch
        {
            "wrong-pid" => Xml().Replace("PID=\"42\"", "PID=\"43\""),
            "dtd" => "<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///missing'>]>" + Xml(),
            "truncated" => Xml()[..^10],
            "schema" => "<unrelated/>",
            "missing-counter" => Xml().Replace("PrivateBytes=\"4096\"", ""),
            "negative" => Xml().Replace("8192", "-1"),
            "no-regions" =>
                "<root Application=\"VMMAP\" Version=\"2.0\" PID=\"42\"><Snapshots><Snapshot><MemoryRegions/></Snapshot></Snapshots></root>",
            _ => throw new Exception(),
        };
        WithFile(xml, path => Should.Throw<Exception>(() => HotelsH5Map.Read(path, 42)));
    }

    [Fact]
    public async Task Partial_stdout_is_drained_but_overflow_cannot_be_reported_as_success()
    {
        using var small = Reader("partial");
        (await HotelsH5Map.ReadBoundedAsync(small, TestContext.Current.CancellationToken)).ShouldBe(
            "partial"
        );
        using var large = Reader(new string('x', 8193));
        await Should.ThrowAsync<InvalidDataException>(() =>
            HotelsH5Map.ReadBoundedAsync(large, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public void Missing_map_is_not_a_successful_empty_map() =>
        Should.Throw<FileNotFoundException>(() =>
            HotelsH5Map.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml"), 42)
        );

    private static StreamReader Reader(string value) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(value)));

    private static string Xml() =>
        "<root Application=\"VMMAP\" Version=\"2.0\" PID=\"42\"><Snapshots><Snapshot Timestamp=\"1\" PageTableSize=\"0\"><MemoryRegions><Region Address=\"4096\" Type=\"999\" UseType=\"999\" Size=\"16384\" Commit=\"8192\" PrivateBytes=\"4096\" PrivateWS=\"512\" ShareableWS=\"512\" SharedWS=\"256\"/></MemoryRegions></Snapshot></Snapshots></root>";

    private static void WithFile(string xml, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "travel-h5-test-" + Guid.NewGuid() + ".xml");
        try
        {
            File.WriteAllText(path, xml);
            action(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
