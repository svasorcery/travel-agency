using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelReadExecutionBoundsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Hotelbeds_entire_read_budget_includes_final_normalization(int delayedResponse)
    {
        var clock = new FinalizationClock();
        var request = Request();
        var wire = new HotelbedsWire(request, clock)
        {
            DelayFinalizationOnResponse = delayedResponse,
        };
        var provider = Hotelbeds(wire, clock);
        var search = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        if (delayedResponse == 1)
        {
            search.Kind.ShouldBe(ExecutionKind.Failed);
            search.Reason.ShouldBe("Timeout");
        }
        else
        {
            var prepared = await provider.PrepareAsync(
                new(request, search.Items.Single()),
                TestContext.Current.CancellationToken
            );
            prepared.Kind.ShouldBe(PreparationKind.Failed);
            prepared.Reason.ShouldBe("Timeout");
        }
    }

    [Fact]
    public async Task Hotelbeds_overflow_keeps_the_same_semantic_prefix_after_wire_permutation()
    {
        var request = Request();
        var first = await Hotelbeds(
                new HotelbedsWire(request, null) { Rates = 201 },
                TimeProvider.System
            )
            .SearchAsync(request, TestContext.Current.CancellationToken);
        var reversed = await Hotelbeds(
                new HotelbedsWire(request, null) { Rates = 201, Reverse = true },
                TimeProvider.System
            )
            .SearchAsync(request, TestContext.Current.CancellationToken);
        first.Kind.ShouldBe(ExecutionKind.Limited);
        first.Items.Length.ShouldBe(200);
        first
            .Items.Select(o => o.Terms.RateId)
            .Order(StringComparer.Ordinal)
            .ShouldBe(reversed.Items.Select(o => o.Terms.RateId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Duffel_M2_preparation_budget_survives_HTTP_completion_and_covers_finalization()
    {
        var clock = new FinalizationClock();
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "quote.json"),
            TestContext.Current.CancellationToken
        );
        var wire = new DuffelWire(json, clock);
        var options = DuffelOptions();
        var source = new DuffelStaysProvider(
            new HttpClient(wire),
            options,
            new(options, clock),
            clock
        );
        var request = Request() with { Area = HotelSearchService.Areas[0] };
        var result = await source.PrepareAsync(
            new(
                request,
                new(
                    "duffel-stays",
                    "Test",
                    "rat_fictional",
                    DuffelStaysProviderTests.Terms() with
                    {
                        Dates = request.Dates,
                    },
                    null,
                    true,
                    null
                )
            ),
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(PreparationKind.Failed);
        result.Reason.ShouldBe("Timeout");
    }

    [Fact]
    public async Task Duffel_M2_rate_cap_uses_semantic_order_before_take()
    {
        var json = JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "rates.json"),
                TestContext.Current.CancellationToken
            )
        )!;
        var rates = json["data"]!["accommodation"]!["rooms"]![0]!["rates"]!.AsArray();
        var template = rates[0]!.DeepClone();
        rates.Clear();
        for (var i = 0; i < 3; i++)
        {
            var rate = template.DeepClone();
            rate["id"] = "rat_owned_" + i;
            rate["total_amount"] = (200 + i).ToString(CultureInfo.InvariantCulture);
            rates.Add(rate);
        }
        var options = DuffelOptions();
        options.Value.Rates = 2;
        var request = Request() with { Area = HotelSearchService.Areas[0] };
        var selection = new SelectedStay(
            request,
            new(
                "duffel-stays",
                "Test",
                "srr_owned",
                DuffelStaysProviderTests.Terms() with
                {
                    Dates = request.Dates,
                },
                null,
                true,
                null
            )
        );
        var source = new DuffelStaysProvider(
            new HttpClient(new DuffelWire(json.ToJsonString(), null)),
            options,
            new(options, TimeProvider.System),
            TimeProvider.System
        );
        var first = await source.LoadRatesAsync(selection, TestContext.Current.CancellationToken);
        var reversedRates = rates.Reverse().Select(r => r!.DeepClone()).ToArray();
        rates.Clear();
        foreach (var rate in reversedRates)
            rates.Add(rate);
        var other = new DuffelStaysProvider(
            new HttpClient(new DuffelWire(json.ToJsonString(), null)),
            options,
            new(options, TimeProvider.System),
            TimeProvider.System
        );
        var second = await other.LoadRatesAsync(selection, TestContext.Current.CancellationToken);
        first.Kind.ShouldBe(ExecutionKind.Limited);
        first.Items.Length.ShouldBe(2);
        first
            .Items.Select(o => o.Terms.RateId)
            .Order(StringComparer.Ordinal)
            .ShouldBe(second.Items.Select(o => o.Terms.RateId).Order(StringComparer.Ordinal));
    }

    private static StaySearch Request() =>
        new(HotelSearchService.Areas[3], new(new(2027, 1, 3), new(2027, 1, 5)), new(1));

    private static IOptions<HotelsOptions> DuffelOptions() =>
        Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                ReceiverEnabled = true,
                ApiKey = "duffel_test_owned_only",
                WebhookSecret = "owned-fictional-secret",
                Aggregation = new()
                {
                    Enabled = true,
                    ExternalUseApproved = true,
                    ApprovedUseScope = "DuffelHotelbedsSearchComparisonBooking",
                    ConfigurationRevision = "owned",
                    ApprovalRevision = "owned",
                },
            }
        );

    private static HotelbedsStayProvider Hotelbeds(HotelbedsWire wire, TimeProvider clock)
    {
        var options = Options.Create(
            new HotelbedsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                Fictional = true,
                AccountAlias = "owned",
                ConfigurationRevision = "owned",
                ApprovalRevision = "owned",
                ApiKey = "owned-fictional-key",
                Secret = "owned-fictional-secret",
            }
        );
        return new(
            new HotelbedsFictionalHttpClient(wire),
            options,
            new(Options.Create(new HotelsOptions()), clock),
            clock
        );
    }

    private sealed class FinalizationClock : TimeProvider
    {
        private readonly FakeTimeProvider inner = new(DateTimeOffset.UtcNow);
        public bool AdvanceOnRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (AdvanceOnRead)
            {
                AdvanceOnRead = false;
                inner.Advance(TimeSpan.FromSeconds(16));
            }
            return inner.GetUtcNow();
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        ) => inner.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class DuffelWire(string json, FinalizationClock? clock) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            if (clock is not null)
                clock.AdvanceOnRead = true;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private sealed class HotelbedsWire(StaySearch request, FinalizationClock? clock)
        : HttpMessageHandler,
            IHotelbedsFictionalTransport
    {
        public int DelayFinalizationOnResponse { get; init; }
        public int Rates { get; init; } = 1;
        public bool Reverse { get; init; }
        private int calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage message,
            CancellationToken ct
        )
        {
            calls++;
            var indices = Enumerable.Range(0, Rates);
            if (Reverse)
                indices = indices.Reverse();
            var rates = indices
                .Select(i => new
                {
                    rateKey = "owned-opaque-" + i,
                    rateClass = "NRF",
                    rateType = "BOOKABLE",
                    net = (200 + i).ToString(CultureInfo.InvariantCulture),
                    paymentType = "AT_WEB",
                    packaging = false,
                    boardCode = "RO",
                    boardName = "ROOM ONLY",
                    rooms = 1,
                    adults = 1,
                    children = 0,
                    cancellationPolicies = Array.Empty<object>(),
                    taxes = new { allIncluded = true, taxes = Array.Empty<object>() },
                })
                .ToArray();
            var hotel = new
            {
                code = 900001,
                name = "Owned Fictional House",
                checkIn = request.Dates.CheckIn.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                ),
                checkOut = request.Dates.CheckOut.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                ),
                currency = "GBP",
                rooms = new[]
                {
                    new
                    {
                        code = "OWN",
                        name = "Owned room",
                        rates,
                    },
                },
            };
            object reply = message.RequestUri!.AbsolutePath.EndsWith(
                "/hotels",
                StringComparison.Ordinal
            )
                ? new
                {
                    hotels = new
                    {
                        checkIn = hotel.checkIn,
                        checkOut = hotel.checkOut,
                        hotels = new[] { hotel },
                    },
                }
                : new { hotel };
            if (clock is not null && calls == DelayFinalizationOnResponse)
                clock.AdvanceOnRead = true;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(reply),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
