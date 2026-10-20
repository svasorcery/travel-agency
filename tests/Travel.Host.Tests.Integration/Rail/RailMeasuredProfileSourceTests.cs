using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Host.Tests.Integration.Rail;

public sealed class RailMeasuredProfileSourceTests
{
    [Fact]
    public async Task Reviewed_streaming_fixture_is_exact_40MiB_with_100000_RF_stations()
    {
        long bytes = 0;
        await using var source = RailMeasuredProfile.OpenCatalogueFixture(count => bytes += count);
        var result = await YandexStationCatalogueParser.ParseAsync(
            source,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        bytes.ShouldBe(41943040);
        result.Value.Stations.Length.ShouldBe(100000);
        result.Value.Stations.ShouldNotContain(x => x.Reference.Code == "s900001");
    }
}
