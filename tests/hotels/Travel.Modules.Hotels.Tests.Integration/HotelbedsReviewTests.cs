using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

// Independent main-agent review: owned HTTP messages only, no DB or shared fixture.
public sealed class HotelbedsReviewTests
{
    [Fact]
    public async Task Contradictory_tax_completeness_cannot_authorize_purchase()
    {
        using var transport = new ReviewTransport { ContradictoryTaxes = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var result = await Provider(client)
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Items.ShouldAllBe(o => !o.Purchasable);
        transport.BookingCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Equivalent_decimal_precision_does_not_invent_an_accepted_to_actual_difference()
    {
        using var transport = new ReviewTransport { DifferentPenaltyPrecision = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var (outcome, terms) = await Book(Provider(client));
        outcome.Kind.ShouldBe(ObservationKind.Confirmed);
        var state = new ReservationState();
        state.Apply(outcome, terms, DateTimeOffset.UtcNow);
        state.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        state.Review.ShouldNotContain(ReviewReason.TermsMismatch);
    }

    [Fact]
    public async Task Duplicate_money_fields_cannot_be_silently_resolved_by_JSON_property_order()
    {
        using var transport = new ReviewTransport { DuplicateMoney = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var result = await Provider(client)
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(ExecutionKind.Failed);
        result.Items.ShouldBeEmpty();
        transport.BookingCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("packaging")]
    [InlineData("hotelMandatory")]
    public async Task Malformed_restriction_flag_cannot_be_treated_as_false(string flag)
    {
        using var transport = new ReviewTransport { MalformedFlag = flag };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var result = await Provider(client)
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Items.ShouldAllBe(o => !o.Purchasable);
        transport.BookingCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Search_cannot_relabel_a_supplier_stay_as_the_requested_stay()
    {
        using var transport = new ReviewTransport { WrongSearchDates = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var result = await Provider(client)
            .SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.Items.ShouldBeEmpty();
        result.Kind.ShouldBe(ExecutionKind.Failed);
        transport.BookingCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Recheck_cannot_silently_change_the_stay_before_consent()
    {
        using var transport = new ReviewTransport { WrongPreparedDates = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var source = Provider(client);
        var request = Request();
        var search = await source.SearchAsync(request, TestContext.Current.CancellationToken);
        var preparation = await source.PrepareAsync(
            new(request, search.Items.Single()),
            TestContext.Current.CancellationToken
        );
        preparation.Kind.ShouldNotBe(PreparationKind.Ready);
        transport.BookingCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("SOMETHING_NEW")]
    public async Task Undocumented_status_with_a_reference_is_not_positive_reservation_evidence(
        string status
    )
    {
        using var transport = new ReviewTransport { BookingStatus = status };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var (outcome, terms) = await Book(Provider(client));
        var state = new ReservationState();
        state.Apply(outcome, terms, DateTimeOffset.UtcNow);
        state.EverReserved.ShouldBeFalse();
        state.Active.ShouldBeFalse();
        state.BoundBookingId.ShouldBeNull();
        transport.BookingCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Explicitly_different_client_reference_cannot_be_attached_to_the_owned_attempt()
    {
        using var transport = new ReviewTransport { ContradictClientReference = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var (outcome, terms) = await Book(Provider(client));
        var state = new ReservationState();
        state.Apply(outcome, terms, DateTimeOffset.UtcNow);
        state.EverReserved.ShouldBeFalse();
        state.BoundBookingId.ShouldBeNull();
        transport.BookingCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Confirmed_reference_without_details_retains_positive_evidence_and_read_identity()
    {
        using var transport = new ReviewTransport { OmitBookingHotel = true };
        using var client = new HotelbedsFictionalHttpClient(transport);
        var (outcome, terms) = await Book(Provider(client));
        var state = new ReservationState();
        state.Apply(outcome, terms, DateTimeOffset.UtcNow);
        state.EverReserved.ShouldBeTrue();
        state.Outcome.ShouldBe(ReservationOutcome.ReservedAwaitingDetails);
        state.BoundBookingId.ShouldBe("OWN-REVIEW");
        transport.BookingCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Local_freshness_does_not_invent_supplier_expiration()
    {
        using var transport = new ReviewTransport();
        using var client = new HotelbedsFictionalHttpClient(transport);
        var source = Provider(client);
        var request = Request();
        var search = await source.SearchAsync(request, TestContext.Current.CancellationToken);
        var offer = search.Items.Single();
        offer.SourceExpiresAt.ShouldBeNull();
        var preparation = await source.PrepareAsync(
            new(request, offer),
            TestContext.Current.CancellationToken
        );
        preparation.Kind.ShouldBe(PreparationKind.Ready);
        preparation.Stay!.SourceExpiresAt.ShouldBeNull();
        preparation.Stay.FreshUntil.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    private static HotelbedsStayProvider Provider(HttpClient client) =>
        new(
            client,
            Options.Create(
                new HotelbedsOptions
                {
                    Enabled = true,
                    BookingEnabled = true,
                    Fictional = true,
                    AccountAlias = "owned-review",
                    ConfigurationRevision = "review-1",
                    ApprovalRevision = "owned-review-1",
                    ApiKey = "owned-review-api-key",
                    Secret = "owned-review-secret",
                }
            ),
            new HotelCapacity(Options.Create(new HotelsOptions()), TimeProvider.System),
            TimeProvider.System
        );

    private static StaySearch Request()
    {
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        return new(HotelSearchService.Areas[3], new(checkIn, checkIn.AddDays(2)), new(1));
    }

    private static async Task<(BookingObservation Outcome, StayTerms Terms)> Book(
        HotelbedsStayProvider source
    )
    {
        var cancellation = TestContext.Current.CancellationToken;
        var request = Request();
        var search = await source.SearchAsync(request, cancellation);
        var preparation = await source.PrepareAsync(
            new(request, search.Items.Single()),
            cancellation
        );
        preparation.Kind.ShouldBe(PreparationKind.Ready);
        var stay = preparation.Stay!;
        var outcome = await source.CreateAsync(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                stay.Terms,
                stay.Context,
                new(
                    [new("Fictional", "Review")],
                    "fictional@example.invalid",
                    "+44000000000",
                    true,
                    true
                )
            ),
            cancellation
        );
        return (outcome, stay.Terms);
    }

    private sealed class ReviewTransport : HttpMessageHandler, IHotelbedsFictionalTransport
    {
        public bool ContradictoryTaxes { get; init; }
        public bool DifferentPenaltyPrecision { get; init; }
        public bool DuplicateMoney { get; init; }
        public string? MalformedFlag { get; init; }
        public bool WrongSearchDates { get; init; }
        public bool WrongPreparedDates { get; init; }
        public string BookingStatus { get; init; } = "CONFIRMED";
        public bool ContradictClientReference { get; init; }
        public bool OmitBookingHotel { get; init; }
        public int BookingCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            var stay = Request();
            var hotel = Hotel(stay);
            if (ContradictoryTaxes)
                hotel["rooms"]![0]!["rates"]![0]!["taxes"]!["taxes"] = new JsonArray(
                    new JsonObject
                    {
                        ["included"] = false,
                        ["amount"] = "10.00",
                        ["currency"] = "GBP",
                    }
                );
            if (MalformedFlag is not null)
                hotel["rooms"]![0]!["rates"]![0]![MalformedFlag] = "true";
            JsonObject response;
            if (path.EndsWith("/hotels", StringComparison.Ordinal))
            {
                var shift = WrongSearchDates ? 1 : 0;
                hotel.Remove("checkIn");
                hotel.Remove("checkOut");
                response = new()
                {
                    ["hotels"] = new JsonObject
                    {
                        ["checkIn"] = stay.Dates.CheckIn.AddDays(shift).ToString("yyyy-MM-dd"),
                        ["checkOut"] = stay.Dates.CheckOut.AddDays(shift).ToString("yyyy-MM-dd"),
                        ["hotels"] = new JsonArray(hotel),
                    },
                };
            }
            else if (path.EndsWith("/checkrates", StringComparison.Ordinal))
            {
                if (WrongPreparedDates)
                {
                    hotel["checkIn"] = stay.Dates.CheckIn.AddDays(1).ToString("yyyy-MM-dd");
                    hotel["checkOut"] = stay.Dates.CheckOut.AddDays(1).ToString("yyyy-MM-dd");
                }
                hotel["rooms"]![0]!["rates"]![0]!["rateType"] = "BOOKABLE";
                response = new() { ["hotel"] = hotel };
            }
            else if (path.EndsWith("/bookings", StringComparison.Ordinal))
            {
                BookingCalls++;
                if (DifferentPenaltyPrecision)
                    hotel["rooms"]![0]!["rates"]![0]!["cancellationPolicies"]![0]!["amount"] =
                        "200.0";
                var submitted = JsonNode.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                )!;
                var booking = new JsonObject
                {
                    ["reference"] = "OWN-REVIEW",
                    ["status"] = BookingStatus,
                    ["clientReference"] = ContradictClientReference
                        ? "other-owned-attempt"
                        : submitted["clientReference"]!.GetValue<string>(),
                    ["totalNet"] = 200.00m,
                    ["currency"] = "GBP",
                };
                if (!OmitBookingHotel)
                    booking["hotel"] = hotel;
                response = new() { ["booking"] = booking };
            }
            else
                throw new InvalidOperationException("Unexpected owned transport operation.");
            var body = response.ToJsonString();
            if (DuplicateMoney)
                body = body.Replace(
                    "\"net\":\"200.00\"",
                    "\"net\":\"1.00\",\"net\":\"200.00\"",
                    StringComparison.Ordinal
                );
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        private static JsonObject Hotel(StaySearch stay) =>
            new()
            {
                ["code"] = 900001,
                ["name"] = "Owned Fictional House",
                ["checkIn"] = stay.Dates.CheckIn.ToString("yyyy-MM-dd"),
                ["checkOut"] = stay.Dates.CheckOut.ToString("yyyy-MM-dd"),
                ["currency"] = "GBP",
                ["rooms"] = new JsonArray(
                    new JsonObject
                    {
                        ["code"] = "DBL.OWN",
                        ["name"] = "Owned fictional room",
                        ["rates"] = new JsonArray(
                            new JsonObject
                            {
                                ["rateKey"] = "owned-review-opaque-key",
                                ["rateClass"] = "NOR",
                                ["rateType"] = "RECHECK",
                                ["net"] = "200.00",
                                ["paymentType"] = "AT_WEB",
                                ["packaging"] = false,
                                ["boardCode"] = "RO",
                                ["boardName"] = "ROOM ONLY",
                                ["rooms"] = 1,
                                ["adults"] = 1,
                                ["children"] = 0,
                                ["rateComments"] = "Owned fictional conditions.",
                                ["cancellationPolicies"] = new JsonArray(
                                    new JsonObject
                                    {
                                        ["amount"] = "200.00",
                                        ["from"] = "2026-11-01T12:00:00+01:00",
                                    }
                                ),
                                ["taxes"] = new JsonObject
                                {
                                    ["allIncluded"] = true,
                                    ["taxes"] = new JsonArray(),
                                },
                            }
                        ),
                    }
                ),
            };
    }
}
