using System.Net;
using System.Text.Json;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Rail.Infrastructure;

[TestOnly]
public sealed class FictionalRailTransport : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.RequestUri?.Host != "api.rasp.yandex-net.ru")
            throw new InvalidOperationException("Fictional Rail destination denied.");
        var path = request.RequestUri.AbsolutePath;
        object body;
        if (path == "/v3.0/copyright/")
            body = new
            {
                copyright = new
                {
                    logo_vm = "<iframe src=\"//yandex.st/rasp/media/apicc/copyright_vert_mono.html\" width=\"240\" height=\"130\"></iframe>",
                    text = "Вымышленные данные для демонстрации Rail. Это не расписание Яндекса.",
                    url = "https://rasp.yandex.ru/",
                },
            };
        else if (path == "/v3.0/stations_list/")
            body = new
            {
                countries = new[]
                {
                    new
                    {
                        codes = new { yandex_code = "l225" },
                        regions = new[]
                        {
                            new
                            {
                                title = "Вымышленная область",
                                settlements = new[]
                                {
                                    new
                                    {
                                        title = "Вымышленный город",
                                        stations = new[]
                                        {
                                            Station("s1", "Учебная Северная"),
                                            Station("s2", "Учебная Южная"),
                                            Station("s3", "Учебная Восточная"),
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        else if (path == "/v3.0/search/")
        {
            var query = request
                .RequestUri.Query.TrimStart('?')
                .Split('&')
                .Select(x => x.Split('=', 2))
                .ToDictionary(
                    x => x[0],
                    x => Uri.UnescapeDataString(x.ElementAtOrDefault(1) ?? ""),
                    StringComparer.Ordinal
                );
            var from = query["from"];
            var to = query["to"];
            var date = query["date"];
            var mode = query["transport_types"];
            body = new
            {
                search = new
                {
                    from = new { code = from, type = "station" },
                    to = new { code = to, type = "station" },
                    date,
                },
                pagination = new
                {
                    total = 1,
                    offset = 0,
                    limit = 50,
                },
                segments = new[]
                {
                    new
                    {
                        from = new { code = from, type = "station" },
                        to = new { code = to, type = "station" },
                        thread = new
                        {
                            uid = "fictional-" + mode,
                            transport_type = mode,
                            number = mode == "train" ? "001" : "6001",
                            title = "Вымышленный рейс",
                            thread_method_link = $"https://api.rasp.yandex-net.ru/v3.0/thread/?uid=fictional-{mode}&date={date}",
                        },
                        has_transfers = false,
                        departure = date + "T10:00:00+03:00",
                        arrival = date + "T12:00:00+03:00",
                        duration = 7200,
                        tickets_info = new
                        {
                            et_marker = false,
                            places = new[]
                            {
                                new
                                {
                                    name = "Учебный тип",
                                    currency = "RUB",
                                    price = new { whole = 1234, cents = 5 },
                                },
                            },
                        },
                    },
                },
            };
        }
        else if (path == "/v3.0/thread/")
        {
            var query = request
                .RequestUri.Query.TrimStart('?')
                .Split('&')
                .Select(x => x.Split('=', 2))
                .ToDictionary(
                    x => x[0],
                    x => Uri.UnescapeDataString(x.ElementAtOrDefault(1) ?? ""),
                    StringComparer.Ordinal
                );
            var date = query["date"];
            var uid = query["uid"];
            body = new
            {
                uid,
                transport_type = uid == "fictional-train" ? "train" : "suburban",
                departure_date = date,
                arrival_date = date,
                stops = new object[]
                {
                    new
                    {
                        station = new
                        {
                            code = query["from"],
                            title = "Учебная Северная",
                            type = "station",
                        },
                        arrival = (string?)null,
                        departure = date + "T10:00:00+03:00",
                        stop_time = (int?)null,
                    },
                    new
                    {
                        station = new
                        {
                            code = "s900003",
                            title = "Учебная Промежуточная",
                            type = "station",
                        },
                        arrival = date + "T11:00:00+03:00",
                        departure = date + "T11:01:00+03:00",
                        stop_time = 60,
                    },
                    new
                    {
                        station = new
                        {
                            code = query["to"],
                            title = "Учебная Южная",
                            type = "station",
                        },
                        arrival = date + "T12:00:00+03:00",
                        departure = (string?)null,
                        stop_time = (int?)null,
                    },
                },
            };
        }
        else
            throw new InvalidOperationException("Fictional Rail method denied.");
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body)),
            }
        );
    }

    private static object Station(string code, string title) =>
        new
        {
            codes = new { yandex_code = code },
            title,
            transport_type = "train",
            station_type = "station",
        };
}
