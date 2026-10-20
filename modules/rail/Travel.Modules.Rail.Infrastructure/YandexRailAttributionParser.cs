using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ErrorOr;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Infrastructure;

public static class YandexRailAttributionParser
{
    public static ErrorOr<RailAttribution> Parse(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = YandexRailScheduleParser.Read(body, 128 * 1024);
            var value = YandexRailScheduleParser.One(document.RootElement, "copyright");
            var html = YandexRailScheduleParser.Text(value, "logo_vm");
            var text = YandexRailScheduleParser.Text(value, "text");
            var url = YandexRailScheduleParser.Text(value, "url");
            if (
                string.IsNullOrWhiteSpace(text)
                || !RailRules.LabelValid(text)
                || url is not ("https://rasp.yandex.ru/" or "http://rasp.yandex.ru/")
                || html.Length > 4096
            )
                throw new JsonException();
            using var xml = XmlReader.Create(
                new StringReader(html),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 4096,
                }
            );
            var root = XDocument.Load(xml).Root ?? throw new JsonException();
            if (root.Name != "iframe" || root.HasElements || !string.IsNullOrWhiteSpace(root.Value))
                throw new JsonException();
            foreach (var attribute in root.Attributes())
            {
                var valid = attribute.Name.LocalName switch
                {
                    "src" => attribute.Value
                        is "//yandex.st/rasp/media/apicc/copyright_vert_mono.html"
                            or "https://yandex.st/rasp/media/apicc/copyright_vert_mono.html",
                    "width" => attribute.Value == "240",
                    "height" => attribute.Value == "130",
                    "frameborder" => attribute.Value == "0",
                    "scrolling" => attribute.Value == "no",
                    "style" => string.Concat(attribute.Value.Where(c => !char.IsWhiteSpace(c)))
                        == "overflow:hidden;border:0;width:240px;height:130px;",
                    _ => false,
                };
                if (!valid || attribute.Name.Namespace != XNamespace.None)
                    throw new JsonException();
            }
            if (
                root.Attribute("src") is null
                || (
                    (
                        root.Attribute("width")?.Value != "240"
                        || root.Attribute("height")?.Value != "130"
                    ) && root.Attribute("style") is null
                )
            )
                throw new JsonException();
            var attribution = new RailAttribution(
                "https://yandex.st/rasp/media/apicc/copyright_vert_mono.html",
                text,
                "https://rasp.yandex.ru/"
            );
            return JsonSerializer.SerializeToUtf8Bytes(attribution).Length <= 4096
                ? attribution
                : Error.Failure("attribution-unavailable");
        }
        catch (Exception error)
            when (error is JsonException or XmlException or InvalidOperationException)
        {
            return Error.Failure("attribution-unavailable");
        }
    }
}
