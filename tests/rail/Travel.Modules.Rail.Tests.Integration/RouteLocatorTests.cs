using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class RouteLocatorTests
{
    [Theory]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=fict%2Buid&date=2026-11-01")]
    [InlineData("api.rasp.yandex-net.ru/v3.0/thread/?date=2026-11-01&uid=fict%2Buid")]
    [InlineData("https://api.rasp.yandex.net/v3.0/thread/?uid=fict%2Buid&date=2026-11-01")]
    public void Lookup_date_is_source_fact_independent_from_boarding(string link)
    {
        var fact = YandexRailRouteLocatorParser.Parse("opaque", "fict+uid", link);
        fact.State.ShouldBe("reported");
        fact.Locator!.SourceLookupDate.ShouldBe("2026-11-01");
    }

    [Theory]
    [InlineData("http://api.rasp.yandex-net.ru/v3.0/thread/?uid=x&date=2026-11-01")]
    [InlineData("https://api.rasp.yandex-net.ru:443/v3.0/thread/?uid=x&date=2026-11-01")]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=x&%75id=x&date=2026-11-01")]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=x&date=2026-11-01&apikey=canary")]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=x&date=2026-02-30")]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=%2578&date=2026-11-01")]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=x+&date=2026-11-01")]
    [InlineData("https://evil.test/v3.0/thread/?uid=x&date=2026-11-01")]
    [InlineData("https://user@api.rasp.yandex-net.ru/v3.0/thread/?uid=x&date=2026-11-01")]
    [InlineData("//api.rasp.yandex-net.ru/v3.0/thread/?uid=x&date=2026-11-01")]
    [InlineData("https://api.rasp.yandex-net.ru/v3.0/thread/?uid=x&date=2026-11-01#x")]
    public void Unsafe_or_ambiguous_locator_is_invalid(string link) =>
        YandexRailRouteLocatorParser.Parse("opaque", "x", link).State.ShouldBe("invalid");
}
