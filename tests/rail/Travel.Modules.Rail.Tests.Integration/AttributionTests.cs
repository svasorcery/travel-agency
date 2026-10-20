using System.Text.Json;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class AttributionTests
{
    public static byte[] Body(string html, string url = "https://rasp.yandex.ru/") =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                copyright = new
                {
                    logo_vm = html,
                    text = "Данные предоставлены Яндекс.Расписаниями",
                    url,
                },
            }
        );

    public const string Safe =
        "<iframe src=\"//yandex.st/rasp/media/apicc/copyright_vert_mono.html\" width=\"240\" height=\"130\" frameborder=\"0\"></iframe>";

    [Fact]
    public void Normalizes_metadata_without_provider_HTML()
    {
        var value = YandexRailAttributionParser.Parse(Body(Safe, "http://rasp.yandex.ru/"));
        value.IsError.ShouldBeFalse();
        value.Value.FrameUrl.ShouldBe(
            "https://yandex.st/rasp/media/apicc/copyright_vert_mono.html"
        );
        value.Value.Url.ShouldBe("https://rasp.yandex.ru/");
    }

    [Theory]
    [InlineData("srcdoc=\"bad\"")]
    [InlineData("onload=\"bad\"")]
    [InlineData("sandbox=\"allow-scripts\"")]
    public void Untrusted_permissions_are_rejected(string attribute) =>
        YandexRailAttributionParser
            .Parse(Body(Safe.Replace("width=", attribute + " width=", StringComparison.Ordinal)))
            .IsError.ShouldBeTrue();

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("https://yаndex.st/rasp/media/apicc/copyright_vert_mono.html")]
    [InlineData("https://yandex.st/rasp/media/apicc/copyright_vert_mono.html?route=s1")]
    public void Nonallowlisted_frame_is_rejected(string url) =>
        YandexRailAttributionParser
            .Parse(
                Body(
                    Safe.Replace(
                        "//yandex.st/rasp/media/apicc/copyright_vert_mono.html",
                        url,
                        StringComparison.Ordinal
                    )
                )
            )
            .IsError.ShouldBeTrue();
}
