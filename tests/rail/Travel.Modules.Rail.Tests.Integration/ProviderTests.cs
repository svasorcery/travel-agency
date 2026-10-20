using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class ProviderTests
{
    private sealed class Monitor : IOptionsMonitor<RailOptions>
    {
        public RailOptions CurrentValue { get; } = new() { Enabled = true, Fictional = true };

        public RailOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<RailOptions, string?> listener) => null;
    }

    private sealed class Terminal : HttpMessageHandler
    {
        public List<string> Paths = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.RequestUri!.Host.ShouldBe("api.rasp.yandex-net.ru");
            Paths.Add(request.RequestUri.AbsolutePath);
            byte[] body = request.RequestUri.AbsolutePath switch
            {
                "/v3.0/copyright/" => AttributionTests.Body(AttributionTests.Safe),
                "/v3.0/stations_list/" => Encoding.UTF8.GetBytes(
                    "{\"countries\":["
                        + CatalogueParserTests.Country(
                            "l225",
                            CatalogueParserTests.Station(),
                            false
                        )
                        + "]}"
                ),
                "/v3.0/search/" => Search(request.RequestUri),
                _ => throw new InvalidOperationException("Nonterminal destination"),
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            );
        }

        private static byte[] Search(Uri uri)
        {
            uri.Query.ShouldContain("transfers=false");
            uri.Query.ShouldContain("transport_types=train");
            uri.Query.ShouldContain("date=2026-10-20");
            uri.Query.ShouldContain("limit=50");
            uri.Query.ShouldNotContain("result_timezone");
            return Encoding.UTF8.GetBytes(ScheduleParserTests.Body(ScheduleParserTests.Row, 1));
        }
    }

    [Fact]
    public async Task Atomic_reference_uses_two_GET_then_search_only_one_GET()
    {
        using var runtime = new RailRuntime(new Monitor());
        var terminal = new Terminal();
        var client = new YandexRailClient(
            new HttpClient(terminal),
            runtime,
            new RailQuotaLedger(TimeProvider.System),
            TimeProvider.System
        );
        var loader = new YandexRailReferenceLoader(client, TimeProvider.System);
        var reference = await loader.LoadAsync(
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        reference.IsError.ShouldBeFalse();
        terminal.Paths.ShouldBe(new[] { "/v3.0/copyright/", "/v3.0/stations_list/" });
        var provider = new YandexRailScheduleProvider(client, TimeProvider.System);
        var result = await provider.SearchAsync(
            ScheduleParserTests.Request,
            "train",
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe("complete");
        terminal.Paths.Count.ShouldBe(3);
        result.Items.Length.ShouldBe(1);
    }
}
