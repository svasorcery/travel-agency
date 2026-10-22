using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class JourneyVerificationTests
{
    private sealed class Terminal(string body) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            request.RequestUri!.Host.ShouldBe("api.rasp.yandex-net.ru");
            request.RequestUri.AbsolutePath.ShouldBe("/v3.0/search/");
            request.RequestUri.Query.ShouldContain("transport_types=train");
            request.RequestUri.Query.ShouldContain("date=2026-10-20");
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
        }
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("missing")]
    [InlineData("malformed-other")]
    [InlineData("conflicting-duplicate")]
    public async Task Fresh_search_parser_keeps_locator_internal_and_dates_independent(
        string scenario
    )
    {
        var row = JsonNode.Parse(ScheduleParserTests.Row)!;
        row["thread"]!["thread_method_link"] =
            "https://api.rasp.yandex.net/v3.0/thread/?uid=fiction-1&date=2026-10-19";
        if (scenario == "missing")
            row["thread"]!.AsObject().Remove("thread_method_link");
        var rows = row.ToJsonString();
        var count = 1;
        if (scenario is "malformed-other" or "conflicting-duplicate")
        {
            var second = row.DeepClone();
            if (scenario == "malformed-other")
            {
                second["thread"]!["uid"] = "other";
                second["thread"]!["thread_method_link"] = "https://evil.test/secret";
            }
            else
                second["thread"]!["thread_method_link"] =
                    "https://api.rasp.yandex.net/v3.0/thread/?uid=fiction-1&date=2026-10-18";
            rows += "," + second.ToJsonString();
            count++;
        }
        var terminal = new Terminal(ScheduleParserTests.Body(rows, count));
        using var runtime = new RailRuntime(
            new StubOptionsMonitor<RailOptions>(
                new()
                {
                    Enabled = true,
                    Fictional = true,
                    JourneyDetailsEnabled = true,
                }
            )
        );
        var provider = new YandexRailJourneyProvider(
            new(new HttpClient(terminal), runtime, new(TimeProvider.System), TimeProvider.System),
            TimeProvider.System
        );
        var page = await provider.VerifyAsync(
            ScheduleParserTests.Request,
            "train",
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        page.Page.Kind.ShouldBe("complete");
        terminal.Calls.ShouldBe(1);
        if (scenario == "missing")
            page.Locators[0].State.ShouldBe("missing");
        else
        {
            page.Locators[0].Locator!.SourceLookupDate.ShouldBe("2026-10-19");
            page.Page.Items[0].Departure.Day.ShouldBe(20);
        }
        if (scenario == "malformed-other")
            page.Locators[1].State.ShouldBe("invalid");
        if (scenario == "conflicting-duplicate")
        {
            page.Locators.Length.ShouldBe(2);
            page.Locators[0].Key.ShouldBe(page.Locators[1].Key);
            page.Locators[0].Locator.ShouldNotBe(page.Locators[1].Locator);
        }
        System.Text.Json.JsonSerializer.Serialize(page.Page).ShouldNotContain("SourceLookupDate");
    }
}
