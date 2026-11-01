using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH5RealXmlTests
{
    [Fact]
    public void Retained_real_P0_parses_all_categories_without_double_counting_nested_regions()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "h5-vmmap-p0.xml.gz");
        Convert
            .ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)))
            .ShouldBe("934DA83366E89991C9A7A3442105BF329E2B8AF5B0BEC5831CDA0880396042E4");
        WithFile(
            Expand(fixture),
            path =>
            {
                var map = HotelsH5Map.Read(path, 22504);
                map.Regions.ShouldBe(2420);
                map.CommittedBytes.ShouldBe(703844352);
                map.PrivateBytes.ShouldBe(267030528);
                map.WorkingSetBytes.ShouldBe(393240576); // PrivateWS + ShareableWS, not + SharedWS again.
                map.Categories.Single(c => c.RawType == "Private Data")
                    .PrivateBytes.ShouldBe(162484224);
                map.Categories.Single(c => c.RawType == "Heap (Private Data)")
                    .PrivateBytes.ShouldBe(97062912);
                var evidence = JsonSerializer.SerializeToElement(map);
                evidence.GetProperty("AllRegions").GetInt32().ShouldBe(7891);
                evidence.GetProperty("LeafRegions").GetInt32().ShouldBe(6083);
                evidence.GetProperty("LeafPrivateBytes").GetInt64().ShouldBe(268611584);
                evidence.GetProperty("ParentPrivateMismatchCount").GetInt32().ShouldBe(284);
            }
        );
    }

    [Fact]
    public void Real_working_set_components_and_both_category_fields_are_not_aliases()
    {
        WithFile(
            Xml(),
            path =>
            {
                var map = HotelsH5Map.Read(path, 42);
                map.CommittedBytes.ShouldBe(8192);
                map.PrivateBytes.ShouldBe(4096);
                map.WorkingSetBytes.ShouldBe(3072);
                map.Types.ShouldBe(new[] { "Future category" });
                var row = JsonSerializer.SerializeToElement(map.Categories.Single());
                row.GetProperty("RawUseType").GetString().ShouldBe("999");
                row.GetProperty("ShareableWorkingSetBytes").GetInt64().ShouldBe(2048);
                row.GetProperty("SharedWorkingSetBytes").GetInt64().ShouldBe(1024);
            }
        );
    }

    [Fact]
    public void System_paths_survive_sanitization_and_collector_cwd_and_temp_belong_to_output()
    {
        var output = Path.Combine(Path.GetTempPath(), "travel-h5-source", "P0.xml");
        var info = HotelsH5Map.StartInfo("vmmap64.exe", 42, output);
        info.Environment["SystemDrive"]
            .ShouldBe(
                Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!
                    .TrimEnd('\\')
            );
        info.Environment["ProgramData"]
            .ShouldBe(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        info.WorkingDirectory.ShouldBe(Path.GetDirectoryName(output));
        info.Environment["TEMP"].ShouldBe(info.WorkingDirectory);
        info.Environment["TMP"].ShouldBe(info.WorkingDirectory);
        info.Environment.Keys.ShouldNotContain("PATH");
        info.Environment.Keys.ShouldNotContain("_NT_SYMBOL_PATH");
        info.Environment.Keys.ShouldNotContain("ANTHROPIC_API_KEY");
        info.Environment.Keys.ShouldNotContain("DOTNET_STARTUP_HOOKS");
    }

    [Fact]
    public void Relative_output_cannot_make_collector_inherit_the_repository_directory() =>
        Should.Throw<InvalidDataException>(() =>
            HotelsH5Map.StartInfo("vmmap64.exe", 42, "P0.xml")
        );

    [Theory]
    [InlineData("missing-use-type")]
    [InlineData("missing-shareable")]
    [InlineData("shared-exceeds-shareable")]
    [InlineData("negative-use-type")]
    [InlineData("bad-address")]
    [InlineData("nested-missing-counter")]
    [InlineData("deep-regions")]
    public void Unsupported_or_malformed_actual_shape_is_rejected(string fault)
    {
        var xml = fault switch
        {
            "missing-use-type" => Xml().Replace("UseType=\"999\"", ""),
            "missing-shareable" => Xml().Replace("ShareableWS=\"2048\"", ""),
            "shared-exceeds-shareable" => Xml().Replace("SharedWS=\"1024\"", "SharedWS=\"4096\""),
            "negative-use-type" => Xml().Replace("UseType=\"999\"", "UseType=\"-1\""),
            "bad-address" => Xml().Replace("Address=\"4096\"", "Address=\"no-address\""),
            "nested-missing-counter" => Xml()
                .Replace("/></MemoryRegions>", "><Region Size=\"1\"/></Region></MemoryRegions>"),
            "deep-regions" => Xml()
                .Replace(
                    "/></MemoryRegions>",
                    ">"
                        + string.Concat(Enumerable.Repeat("<Region>", 30))
                        + string.Concat(Enumerable.Repeat("</Region>", 30))
                        + "</Region></MemoryRegions>"
                ),
            _ => throw new InvalidOperationException(),
        };
        WithFile(xml, path => Should.Throw<InvalidDataException>(() => HotelsH5Map.Read(path, 42)));
    }

    [Theory]
    [InlineData("snapshots")]
    [InlineData("regions")]
    [InlineData("malformed-second-regions")]
    public void Duplicate_containers_must_not_hide_a_second_partial_or_malformed_map(string kind)
    {
        var document = System.Xml.Linq.XDocument.Parse(Xml());
        var snapshots = document.Root!.Element("Snapshots")!;
        var snapshot = snapshots.Element("Snapshot")!;
        var regions = snapshot.Element("MemoryRegions")!;
        if (kind == "snapshots")
            document.Root.Add(new System.Xml.Linq.XElement(snapshots));
        else if (kind == "regions")
            snapshot.Add(new System.Xml.Linq.XElement(regions));
        else
            snapshot.Add(
                new System.Xml.Linq.XElement(
                    "MemoryRegions",
                    new System.Xml.Linq.XElement("Region")
                )
            );
        WithFile(
            document.ToString(),
            path => Should.Throw<InvalidDataException>(() => HotelsH5Map.Read(path, 42))
        );
    }

    internal static string Xml() =>
        "<root Application=\"VMMAP\" Version=\"2.0\" PID=\"42\"><Snapshots><Snapshot Timestamp=\"1\" PageTableSize=\"0\"><MemoryRegions><Region Address=\"4096\" Type=\"Future category\" UseType=\"999\" Size=\"16384\" Commit=\"8192\" PrivateBytes=\"4096\" PrivateWS=\"1024\" ShareableWS=\"2048\" SharedWS=\"1024\"/></MemoryRegions></Snapshot></Snapshots></root>";

    private static string Expand(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var text = new StreamReader(gzip);
        return text.ReadToEnd();
    }

    private static void WithFile(string xml, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "travel-h5-real-" + Guid.NewGuid() + ".xml");
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
