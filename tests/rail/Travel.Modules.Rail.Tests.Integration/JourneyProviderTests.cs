using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Rail.Infrastructure;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class JourneyProviderTests
{
    private sealed class HeaderLimitFault : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            throw new HttpRequestException(
                HttpRequestError.ConfigurationLimitExceeded,
                "private-header-limit-canary"
            );
    }

    [Fact]
    public async Task Socket_header_limit_fault_normalizes_only_thread_to_output_limit_and_preserves_M1_transport_reason()
    {
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
        var client = new YandexRailClient(
            new HttpClient(new HeaderLimitFault()),
            runtime,
            new(TimeProvider.System),
            TimeProvider.System
        );
        var provider = new YandexRailJourneyProvider(client, TimeProvider.System);
        var result = await provider.RouteAsync(
            RouteParserTests.Journey,
            new("yandex-rasp", "fictional+uid", "2026-11-01"),
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.Reasons.ShouldContain("output-limit");
        result.ReceivedAt.ShouldBeNull();
        (
            await client.GetAsync(
                "search",
                "",
                runtime.Current,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("transport");
    }

    [Fact]
    public async Task Thread_header_overflow_uses_route_output_limit_without_claiming_full_body_receive()
    {
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
        var terminal = new Terminal { OversizeHeader = true };
        var provider = new YandexRailJourneyProvider(
            new(new HttpClient(terminal), runtime, new(TimeProvider.System), TimeProvider.System),
            TimeProvider.System
        );
        var result = await provider.RouteAsync(
            RouteParserTests.Journey,
            new("yandex-rasp", "fictional+uid", "2026-11-01"),
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.Reasons.ShouldContain("output-limit");
        result.ReceivedAt.ShouldBeNull();
        terminal.Calls.ShouldBe(1);
    }

    private sealed class Terminal : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool OversizeHeader;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            request.RequestUri!.Host.ShouldBe("api.rasp.yandex-net.ru");
            request.RequestUri.AbsolutePath.ShouldBe("/v3.0/thread/");
            request.RequestUri.Query.ShouldContain("uid=fictional%2Buid");
            request.RequestUri.Query.ShouldContain("date=2026-11-01");
            request.RequestUri.Query.ShouldNotContain("result_timezone");
            request.RequestUri.Query.ShouldNotContain("apikey");
            request.Headers.GetValues("Authorization").Single().ShouldBe("fictional-no-key");
            var response = new HttpResponseMessage(Status)
            {
                Content = new StringContent(
                    RouteParserTests.Wire().ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            if (OversizeHeader)
                response.Headers.TryAddWithoutValidation("X-Fictional", new string('x', 16385));
            return Task.FromResult(response);
        }
    }

    [Theory]
    [InlineData(200, "complete")]
    [InlineData(401, "failed")]
    [InlineData(403, "failed")]
    [InlineData(429, "failed")]
    public async Task Thread_is_one_fixed_host_GET_with_source_lookup_date(int status, string kind)
    {
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
        var terminal = new Terminal { Status = (HttpStatusCode)status };
        var provider = new YandexRailJourneyProvider(
            new(new HttpClient(terminal), runtime, new(TimeProvider.System), TimeProvider.System),
            TimeProvider.System
        );
        var result = await provider.RouteAsync(
            RouteParserTests.Journey,
            new("yandex-rasp", "fictional+uid", "2026-11-01"),
            runtime.Current,
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(kind);
        terminal.Calls.ShouldBe(1);
        if (status != 200)
            result.ReceivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Remote_quota_failure_closes_next_thread_before_dispatch_without_refund_or_retry()
    {
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
        var terminal = new Terminal { Status = HttpStatusCode.TooManyRequests };
        var ledger = new RailQuotaLedger(TimeProvider.System);
        var provider = new YandexRailJourneyProvider(
            new(new HttpClient(terminal), runtime, ledger, TimeProvider.System),
            TimeProvider.System
        );
        var locator = new Travel.Modules.Rail.Application.RailRouteLocator(
            "yandex-rasp",
            "fictional+uid",
            "2026-11-01"
        );
        (
            await provider.RouteAsync(
                RouteParserTests.Journey,
                locator,
                runtime.Current,
                TestContext.Current.CancellationToken
            )
        ).Kind.ShouldBe("failed");
        (
            await provider.RouteAsync(
                RouteParserTests.Journey,
                locator,
                runtime.Current,
                TestContext.Current.CancellationToken
            )
        ).Kind.ShouldBe("skipped");
        terminal.Calls.ShouldBe(1);
        ledger.TryDebit(100000, 100000).ShouldBeFalse();
    }
}
