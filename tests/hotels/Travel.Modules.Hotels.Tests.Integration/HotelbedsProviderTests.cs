using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

// No collection/database fixture: only owned in-memory wire transport.
public sealed class HotelbedsProviderTests
{
    [Theory]
    [InlineData("NRF", true, "NonRefundable")]
    [InlineData("NOR", false, "Unknown")]
    [InlineData("PAQ", false, "Unknown")]
    public async Task Explicit_NRF_differs_from_missing_policy_and_package_is_unsupported(
        string rateClass,
        bool purchasable,
        string kind
    )
    {
        var handler = new OwnedHandler { RateClass = rateClass, NoPolicies = true };
        var result = await Provider(handler)
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Items.Single().Terms.Cancellation.Kind.ShouldBe(kind);
        result.Items.Single().Purchasable.ShouldBe(purchasable);
    }

    [Fact]
    public async Task Changed_supplier_dates_refuse_preparation()
    {
        var handler = new OwnedHandler();
        var provider = Provider(handler);
        var request = Request();
        var found = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        handler.WrongDates = true;
        (
            await provider.PrepareAsync(
                new(request, found.Items[0]),
                TestContext.Current.CancellationToken
            )
        ).Kind.ShouldBe(PreparationKind.Failed);
        handler.Bookings.ShouldBe(0);
    }

    [Theory]
    [InlineData("MYSTERY", false, ObservationKind.Unknown)]
    [InlineData("CONFIRMED", true, ObservationKind.Unknown)]
    public async Task Undocumented_status_or_contradictory_correlation_is_not_owned_positive(
        string status,
        bool wrongReference,
        ObservationKind expected
    )
    {
        var handler = new OwnedHandler { Status = status, WrongReference = wrongReference };
        var provider = Provider(handler);
        var request = Request();
        var found = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        var prepared = (
            await provider.PrepareAsync(
                new(request, found.Items[0]),
                TestContext.Current.CancellationToken
            )
        ).Stay!;
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        var outcome = await provider.CreateAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), prepared.Terms, prepared.Context, party),
            TestContext.Current.CancellationToken
        );
        outcome.Kind.ShouldBe(expected);
        outcome.Facts.ShouldBeNull();
    }

    [Fact]
    public async Task Fictional_RECHECK_full_path_keeps_key_private_and_books_once()
    {
        var handler = new OwnedHandler();
        var provider = Provider(handler);
        var request = Request();
        var found = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        found.Kind.ShouldBe(ExecutionKind.Complete);
        found.Items.Length.ShouldBe(1);
        found.Items[0].ConditionEvidence!.Cancellation[0].AmountKind.ShouldBe("Penalty");
        found.Items[0].ConditionEvidence!.Cancellation[0].Boundary.ShouldBe("From");
        JsonSerializer.Serialize(found).ShouldNotContain("owned-opaque-rate-key");
        var prepared = await provider.PrepareAsync(
            new(request, found.Items[0]),
            TestContext.Current.CancellationToken
        );
        prepared.Kind.ShouldBe(PreparationKind.Ready);
        handler.Bookings.ShouldBe(0);
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        var outcome = await provider.CreateAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), prepared.Stay!.Terms, prepared.Stay.Context, party),
            TestContext.Current.CancellationToken
        );
        outcome.Kind.ShouldBe(ObservationKind.Confirmed);
        outcome.Facts!.Actual!.Total.ShouldBe(new StayMoney(200, "GBP"));
        handler.Bookings.ShouldBe(1);
        handler.Paths.ShouldBe([
            "/hotel-api/1.0/hotels",
            "/hotel-api/1.0/checkrates",
            "/hotel-api/1.0/bookings",
        ]);
    }

    [Fact]
    public async Task Missing_tax_feature_does_not_become_zero_or_purchase()
    {
        var handler = new OwnedHandler { Taxes = false };
        var provider = Provider(handler);
        var found = await provider.SearchAsync(Request(), TestContext.Current.CancellationToken);
        found.Items.Single().Purchasable.ShouldBeFalse();
        found.Items.Single().Terms.MandatoryLocalPayments.ShouldBeNull();
        handler.Bookings.ShouldBe(0);
    }

    [Fact]
    public async Task Actual_unapproved_profile_is_skipped_without_dispatch()
    {
        var handler = new OwnedHandler();
        var options = Settings();
        options.Fictional = false;
        options.ExternalUseApproved = false;
        var provider = Provider(handler, options);
        var result = await provider.SearchAsync(
            Request() with
            {
                Area = HotelSearchService.Areas[0],
            },
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(ExecutionKind.Unsupported);
        result.Reason.ShouldBe("RightsNotCleared");
        result.Attempted.ShouldBeFalse();
        handler.Paths.ShouldBeEmpty();
    }

    private static HotelbedsOptions Settings() =>
        new()
        {
            Enabled = true,
            BookingEnabled = true,
            Fictional = true,
            AccountAlias = "owned",
            ConfigurationRevision = "config-1",
            ApprovalRevision = "owned-1",
            ApiKey = "owned-fictional-key",
            Secret = "owned-fictional-secret",
        };

    private static HotelbedsStayProvider Provider(
        OwnedHandler handler,
        HotelbedsOptions? settings = null
    ) =>
        new(
            new HotelbedsFictionalHttpClient(handler),
            Options.Create(settings ?? Settings()),
            new HotelCapacity(Options.Create(new HotelsOptions()), TimeProvider.System),
            TimeProvider.System
        );

    private static StaySearch Request() =>
        new(
            HotelSearchService.Areas[3],
            new(
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(9)
            ),
            new(1)
        );

    private sealed class OwnedHandler : HttpMessageHandler, IHotelbedsFictionalTransport
    {
        public bool Taxes { get; init; } = true;
        public bool WrongDates { get; set; }
        public string Status { get; init; } = "CONFIRMED";
        public bool WrongReference { get; init; }
        public string RateClass { get; init; } = "NOR";
        public bool NoPolicies { get; init; }
        public int Bookings { get; private set; }
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            request.Headers.GetValues("X-Signature").Single().Length.ShouldBe(64);
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var search = Request();
            var rate = new
            {
                rateKey = "owned-opaque-rate-key",
                rateClass = RateClass,
                rateType = path.EndsWith("hotels", StringComparison.Ordinal)
                    ? "RECHECK"
                    : "BOOKABLE",
                net = "200.00",
                paymentType = "AT_WEB",
                packaging = false,
                boardCode = "RO",
                boardName = "ROOM ONLY",
                rooms = 1,
                adults = 1,
                children = 0,
                cancellationPolicies = new[]
                {
                    new { amount = "200.00", from = "2026-11-01T12:00:00+01:00" },
                }
                    .Take(NoPolicies ? 0 : 1)
                    .ToArray(),
                taxes = Taxes ? new { allIncluded = true, taxes = Array.Empty<object>() } : null,
            };
            var hotel = new
            {
                code = 900001,
                name = "Owned Fictional House",
                checkIn = search.Dates.CheckIn.AddDays(WrongDates ? 1 : 0).ToString("yyyy-MM-dd"),
                checkOut = search.Dates.CheckOut.ToString("yyyy-MM-dd"),
                currency = "GBP",
                rooms = new[]
                {
                    new
                    {
                        code = "DBL.OWN",
                        name = "Fictional double",
                        rates = new[] { rate },
                    },
                },
            };
            object response;
            if (path.EndsWith("hotels", StringComparison.Ordinal))
                response = new
                {
                    hotels = new
                    {
                        hotels = new[] { hotel },
                        checkIn = hotel.checkIn,
                        checkOut = hotel.checkOut,
                    },
                };
            else if (path.EndsWith("checkrates", StringComparison.Ordinal))
            {
                body.ShouldContain("owned-opaque-rate-key");
                response = new { hotel };
            }
            else
            {
                Bookings++;
                using var json = JsonDocument.Parse(body);
                json.RootElement.GetProperty("clientReference")
                    .GetString()!
                    .Length.ShouldBeLessThanOrEqualTo(20);
                response = new
                {
                    booking = new
                    {
                        reference = "OWN-12345",
                        clientReference = WrongReference
                            ? "other"
                            : json.RootElement.GetProperty("clientReference").GetString(),
                        status = Status,
                        totalNet = 200.00m,
                        currency = "GBP",
                        hotel,
                    },
                };
            }
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
