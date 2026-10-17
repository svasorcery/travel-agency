using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class DuffelStaysProviderTests
{
    [Fact]
    public async Task Unsupported_is_same_offline_and_direct_without_HTTP()
    {
        var handler = new Handler("{}");
        var provider = Provider(handler);
        var request = Request() with { Area = HotelSearchService.Areas[3] };
        var support = provider.GetSupport(request);
        support.Supported.ShouldBeFalse();
        (
            await provider.SearchAsync(request, TestContext.Current.CancellationToken)
        ).Reason.ShouldBe(support.Reason);
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("{\"data\":{\"results\":[]}}", ExecutionKind.Complete)]
    [InlineData("{\"data\":{\"results\":[", ExecutionKind.Failed)]
    public async Task Empty_is_distinct_from_malformed(string json, ExecutionKind expected)
    {
        var result = await Provider(new(json))
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(expected);
        result.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(200, ObservationKind.ReservedWithoutDetails)]
    [InlineData(202, ObservationKind.Pending)]
    [InlineData(503, ObservationKind.Unknown)]
    public async Task HTTP_status_alone_does_not_invent_booking_or_no_creation(
        int status,
        ObservationKind expected
    )
    {
        var provider = Provider(
            new("{\"data\":{\"message\":\"fictional\"}}", (HttpStatusCode)status)
        );
        var observation = await provider.CreateAsync(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Terms(),
                new(1, "{\"QuoteId\":\"quo_fictional\",\"RateId\":\"rat_fictional\"}"),
                new(
                    [new("Fictional", "Traveller")],
                    "fictional@example.invalid",
                    "+44000000000",
                    true,
                    true
                )
            ),
            TestContext.Current.CancellationToken
        );
        observation.Kind.ShouldBe(expected);
        observation.Facts.ShouldBeNull();
    }

    internal static StaySearch Request() =>
        new(
            HotelSearchService.Areas[0],
            new(
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(4)
            ),
            new(1)
        );

    internal static StayTerms Terms() =>
        new(
            new("acc_fictional", "Fictional hotel", null, null, null),
            "Double",
            "rat_fictional",
            Request().Dates,
            new(1),
            new(120.25m, "GBP"),
            [],
            [],
            new("NonRefundable", []),
            [],
            true,
            "ImmediateTest"
        );

    internal static DuffelStaysProvider Provider(Handler handler)
    {
        var options = Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                ReceiverEnabled = true,
                ApiKey = "duffel_test_fictional_only",
                WebhookSecret = "fictional-webhook-secret",
            }
        );
        return new(
            new HttpClient(handler),
            options,
            new(options, TimeProvider.System),
            TimeProvider.System
        );
    }

    internal sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
