using System.Text;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class CatalogueParserTests
{
    public static string Station(
        string code = "s1",
        string transport = "train",
        string type = "station"
    ) =>
        $$$"""{"codes":{"yandex_code":"{{{code}}}"},"title":"Станция","transport_type":"{{{transport}}}","station_type":"{{{type}}}"}""";

    public static string Country(string code, string stations, bool metadataFirst) =>
        metadataFirst
            ? $$$"""{"codes":{"yandex_code":"{{{code}}}"},"regions":[{"title":"Область","settlements":[{"title":"Город","stations":[{{{stations}}}]}]}]}"""
            : $$$"""{"regions":[{"settlements":[{"stations":[{{{stations}}}],"title":"Город"}],"title":"Область"}],"codes":{"yandex_code":"{{{code}}}"}}""";

    public static async Task<ErrorOr.ErrorOr<Travel.Modules.Rail.Application.CatalogueData>> Parse(
        string body
    ) =>
        await YandexStationCatalogueParser.ParseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(body)),
            default
        );

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Metadata_order_does_not_lose_RF_station_or_labels(bool first)
    {
        var result = await Parse(
            "{\"countries\":["
                + Country("l1", Station("s2"), !first)
                + ","
                + Country("l225", Station(), first)
                + "]}"
        );
        result.IsError.ShouldBeFalse();
        result.Value.Stations.ShouldHaveSingleItem();
        result.Value.Stations[0].Settlement.ShouldBe("Город");
        result.Value.Stations[0].Region.ShouldBe("Область");
        result.Value.Stations[0].Reference.Code.ShouldBe("s1");
    }

    [Fact]
    public async Task Unknown_RF_type_is_limited_not_false_complete()
    {
        var result = await Parse(
            "{\"countries\":["
                + Country("l225", Station() + "," + Station("s2", "future"), false)
                + "]}"
        );
        result.Value.Limited.ShouldBeTrue();
        result.Value.Stations.Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    [InlineData("{\"countries\":[]}")]
    [InlineData("{\"countries\":null}")]
    public async Task No_RF_is_unavailable(string body) =>
        (await Parse(body)).IsError.ShouldBeTrue();

    [Fact]
    public async Task Conflicting_duplicate_station_ref_rejects_whole_snapshot()
    {
        var other = Station().Replace("Станция", "Иная", StringComparison.Ordinal);
        (
            await Parse("{\"countries\":[" + Country("l225", Station() + "," + other, false) + "]}")
        ).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Excess_token_and_depth_reject_without_truncation()
    {
        (
            await Parse("{\"countries\":[],\"unknown\":\"" + new string('x', 16385) + "\"}")
        ).IsError.ShouldBeTrue();
        (await Parse(new string('[', 33) + "0" + new string(']', 33))).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Extension_named_stations_cannot_inject_RF_station()
    {
        var country = Country("l225", Station(), false);
        var extension = "\"extension\":{\"stations\":[" + Station("s99") + "]},";
        var result = await Parse("{\"countries\":[" + country.Insert(1, extension) + "]}");
        result.Value.Stations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task More_than_100000_known_bus_records_do_not_consume_RF_station_bound()
    {
        var buses = string.Join(',', Enumerable.Repeat(Station("s9", "bus"), 100001));
        var result = await Parse(
            "{\"countries\":[" + Country("l225", buses + "," + Station(), false) + "]}"
        );
        result.IsError.ShouldBeFalse();
        result.Value.Stations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Exactly_100000_stations_succeed_next_record_rejects_entire_candidate()
    {
        var stations = string.Join(',', Enumerable.Range(1, 100000).Select(x => Station("s" + x)));
        var first = await Parse("{\"countries\":[" + Country("l225", stations, false) + "]}");
        first.IsError.ShouldBeFalse();
        first.Value.Stations.Length.ShouldBe(100000);
        (
            await Parse(
                "{\"countries\":["
                    + Country("l225", stations + "," + Station("s100001"), false)
                    + "]}"
            )
        ).IsError.ShouldBeTrue();
    }
}
