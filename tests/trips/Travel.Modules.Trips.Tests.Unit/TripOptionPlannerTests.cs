using System.Text.Json;
using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Core.Options;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripOptionPlannerTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 10, 28);
    private static readonly ValidatedItineraryFacts Facts = ItineraryTestFacts
        .Input("saint-petersburg", "spb-outdoor-profile-v1")
        .Build()
        .Value;
    private static OptionCatalogue Catalogue => FictionalOptionCatalogue.For("A", 2);
    private static TripBriefInput Input =>
        new(
            "saint-petersburg",
            "moscow",
            Start,
            Start.AddDays(2),
            2,
            "RUB",
            null,
            ["architecture"],
            "relaxed"
        );

    private static TripOptionPreview Preview(
        OptionCatalogue catalogue,
        TripBriefInput? input = null
    ) =>
        OptionPlanner
            .Preview(
                TripBrief.Create(input ?? Input).Value,
                Facts,
                AsOf,
                catalogue,
                TestContext.Current.CancellationToken
            )
            .Value;

    private static OptionCharge Charge(
        string component,
        string amount,
        string currency = "RUB",
        OptionChargeKind kind = OptionChargeKind.TravelerMandatory
    ) => new(component, kind, PlanningMoney.Create(amount, currency).Value);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Authored_catalogue_changes_only_the_outbound_times_of_RT1(int adults)
    {
        var a = FictionalOptionCatalogue.For("A", adults);
        var b = FictionalOptionCatalogue.For("B", adults);
        a.Flights.Select(f => f.Id).ShouldBe(["RT1", "RT2", "RT3", "RT4"]);
        a.Hotels.Select(h => h.Id).ShouldBe(["H1", "H2", "H3"]);
        var first = a.Flights[0];
        first.Outbound.ShouldBe(new("SVO", "LED", Local(28, 7, 30), Local(28, 9, 0)));
        first.Inbound.ShouldBe(new("LED", "SVO", Local(30, 11, 0), Local(30, 12, 30)));
        first.Adults.ShouldBe(adults);
        first.Charges.Single().Money!.Amount.ShouldBe(adults == 2 ? "18000.00" : "9000.00");
        a.Flights[1].Adults.ShouldBe(1);
        a.Hotels[0].Nights.ShouldBe([Start, Start.AddDays(1)]);
        a.Hotels[0].Adults.ShouldBe(adults);
        a.Hotels[0].Charges[0].Money!.Amount.ShouldBe("12000.00");
        a.Hotels[1].Charges[0].Money!.Amount.ShouldBe("14000.00");
        a.Flights[2].Inbound!.DepartsAt.ShouldBe(Local(29, 11, 0));
        a.Hotels[2].Nights.ShouldBe([Start]);
        // Deep equality of the entire inventory, including prices, after exactly two edits.
        var expected = a with
        {
            Version = "B",
            Flights =
            [
                first with
                {
                    Outbound = first.Outbound with
                    {
                        DepartsAt = Local(28, 18, 30),
                        ArrivesAt = Local(28, 20, 0),
                    },
                },
                .. a.Flights.Skip(1),
            ],
        };
        JsonSerializer.Serialize(b).ShouldBe(JsonSerializer.Serialize(expected));
    }

    [Theory]
    [InlineData("return", "flightUnavailable")]
    [InlineData("airport", "flightUnavailable")]
    [InlineData("wrongOffset", "flightUnavailable")]
    [InlineData("flightDate", "flightUnavailable")]
    [InlineData("flightDuration", "flightUnavailable")]
    [InlineData("seats", "flightUnavailable")]
    [InlineData("night", "hotelUnavailable")]
    [InlineData("capacity", "hotelUnavailable")]
    [InlineData("guests", "hotelUnavailable")]
    [InlineData("checkIn", "timeIncompatible")]
    [InlineData("checkOut", "timeIncompatible")]
    [InlineData("budget", "budgetExceeded")]
    public void Rejects_an_incompatible_component_with_a_specific_reason(
        string change,
        string reason
    )
    {
        var f = Catalogue.Flights[0];
        var h = Catalogue.Hotels[0];
        f = change switch
        {
            "return" => f with { Inbound = null },
            "airport" => f with { Outbound = f.Outbound with { FromAirport = "LED" } },
            "wrongOffset" => f with
            {
                Outbound = f.Outbound with { ArrivesAt = f.Outbound.ArrivesAt.ToUniversalTime() },
            },
            "flightDate" => f with
            {
                Inbound = f.Inbound! with { ArrivesAt = f.Inbound.ArrivesAt.AddDays(1) },
            },
            "flightDuration" => f with
            {
                Outbound = f.Outbound with { ArrivesAt = f.Outbound.DepartsAt },
            },
            "seats" => f with { Adults = 1 },
            _ => f,
        };
        h = change switch
        {
            "night" => h with { Nights = [Start, Start] },
            "capacity" => h with { Capacity = 1 },
            "guests" => h with { Adults = 1 },
            "checkIn" => h with { EarliestCheckIn = new(6, 0), LatestCheckIn = new(10, 29) },
            "checkOut" => h with { EarliestCheckOut = new(8, 1) },
            _ => h,
        };
        var result = Preview(
            new("A", [f], [h]),
            Input with
            {
                TotalBudget = change == "budget" ? "29999.99" : null,
            }
        );
        result.Outcome.ShouldBe("noMatch");
        result.Reasons.ShouldBe([reason]);
        result.Option.ShouldBeNull();
    }

    [Theory]
    [InlineData("day", "dateRangeUnsupported")]
    [InlineData("fourDays", "dateRangeUnsupported")]
    [InlineData("group", "groupUnsupported")]
    [InlineData("origin", "directionUnsupported")]
    [InlineData("future", "horizonUnsupported")]
    [InlineData("past", "horizonUnsupported")]
    public void Valid_manual_briefs_can_be_outside_the_pilot(string change, string reason)
    {
        var input = change switch
        {
            "day" => Input with { EndDate = Start },
            "fourDays" => Input with { EndDate = Start.AddDays(3) },
            "group" => Input with { Adults = 3 },
            "origin" => Input with { OriginId = "kaliningrad" },
            "future" => Input with { StartDate = Start.AddDays(30), EndDate = Start.AddDays(31) },
            _ => Input with { StartDate = Start.AddDays(-2), EndDate = Start.AddDays(-1) },
        };
        var result = Preview(FictionalOptionCatalogue.For("A", input.Adults), input);
        result.Outcome.ShouldBe("unsupported");
        result.Reasons.ShouldBe([reason]);
    }

    [Fact]
    public void Empty_origin_expired_evidence_unbounded_catalogue_and_cancellation_fail_closed()
    {
        var brief = TripBrief.Create(Input).Value;
        OptionPlanner
            .Preview(
                TripBrief.Create(Input with { OriginId = null }).Value,
                Facts,
                AsOf,
                Catalogue,
                TestContext.Current.CancellationToken
            )
            .FirstError.Code.ShouldBe(TripErrors.InvalidInput.Code);
        OptionPlanner
            .Preview(
                TripBrief
                    .Create(
                        Input with
                        {
                            StartDate = DateOnly
                                .FromDateTime(Facts.ReleaseValidUntil.UtcDateTime)
                                .AddDays(1),
                            EndDate = DateOnly
                                .FromDateTime(Facts.ReleaseValidUntil.UtcDateTime)
                                .AddDays(3),
                        }
                    )
                    .Value,
                Facts,
                Facts.ReleaseValidUntil,
                Catalogue,
                TestContext.Current.CancellationToken
            )
            .FirstError.Code.ShouldBe("Trips.OptionUnavailable");
        OptionPlanner
            .Preview(
                brief,
                Facts,
                AsOf,
                Catalogue with
                {
                    Flights = [.. Catalogue.Flights, Catalogue.Flights[0]],
                },
                TestContext.Current.CancellationToken
            )
            .FirstError.Code.ShouldBe("Trips.OptionUnavailable");
        Should.Throw<OperationCanceledException>(() =>
            OptionPlanner.Preview(brief, Facts, AsOf, Catalogue, new(true))
        );
    }

    [Theory]
    [InlineData(null, "A", "moreInterests", "30000.00")]
    [InlineData("30000", "A", "moreInterests", "30000.00")]
    [InlineData("25000", "B", "onlyCompatibleOption", "24000.00")]
    public void Preferences_rank_before_price_and_budget_filters_known_components(
        string? budget,
        string winner,
        string why,
        string total
    )
    {
        var brief = Input with
        {
            EndDate = Start.AddDays(1),
            TotalBudget = budget,
            Interests = ["architecture", "culture"],
        };
        var f = Catalogue.Flights[2];
        var a = f with
        {
            Id = "A",
            Charges =
            [
                Charge("flightBase", "18000"),
                new("localFee", OptionChargeKind.UnknownMandatory, null),
            ],
        };
        var b = f with
        {
            Id = "B",
            Outbound = new("SVO", "LED", Local(28, 13, 45), Local(28, 15, 15)),
            Charges = [Charge("flightBase", "12000")],
        };
        var hotel = Catalogue.Hotels[2] with { Charges = [Charge("hotelBase", "12000")] };
        var result = Preview(new("A", [a, b], [hotel]), brief).Option.ShouldNotBeNull();
        result.Flight.Id.ShouldBe(winner);
        result.CoveredInterests.Count.ShouldBe(winner == "A" ? 2 : 1);
        result.SelectionReason.ShouldBe(why);
        result.Price.KnownSubtotal.ShouldBe(total);
        result.Price.FlightAndStayComplete.ShouldBe(winner == "B");
        result.Price.BudgetStatus.ShouldBe(budget is null ? "notSpecified" : "indeterminate");
        if (winner == "A")
            result.Price.Components.ShouldContain(l =>
                l.Kind == "unknownMandatory" && l.Amount == null
            );
        JsonSerializer
            .Serialize(Preview(new("A", [b, a], [hotel]), brief).Option)
            .ShouldBe(JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("included", "30000.00", true)]
    [InlineData("extra", "31234.56", true)]
    [InlineData("feeUnknown", "30000.00", false)]
    [InlineData("foreign", "18000.00", false)]
    [InlineData("net", "18000.00", false)]
    public void Price_is_for_whole_stay_and_keeps_each_component_meaning(
        string kind,
        string subtotal,
        bool complete
    )
    {
        var h = Catalogue.Hotels[0];
        OptionCharge[] charges = kind switch
        {
            "included" =>
            [
                Charge("hotelBase", "12000"),
                Charge("includedTax", "1200", kind: OptionChargeKind.Included),
            ],
            "extra" => [Charge("hotelBase", "12000"), Charge("localFee", "1234.56")],
            "feeUnknown" =>
            [
                Charge("hotelBase", "12000"),
                new("localFee", OptionChargeKind.UnknownMandatory, null),
            ],
            "foreign" => [Charge("hotelBase", "120", "EUR")],
            _ => [Charge("hotelBase", "10000", kind: OptionChargeKind.SupplierNet)],
        };
        var catalogue = new OptionCatalogue(
            "A",
            [Catalogue.Flights[0]],
            [h with { Charges = charges }]
        );
        var result = Preview(catalogue, Input with { TotalBudget = subtotal })
            .Option.ShouldNotBeNull();
        result.Price.KnownSubtotal.ShouldBe(subtotal);
        result.Price.FlightAndStayComplete.ShouldBe(complete);
        result.Price.BudgetStatus.ShouldBe("indeterminate");
        if (kind == "foreign")
            result.Price.Components.ShouldContain(c => c.Currency == "EUR" && c.Amount == "120.00");
        if (kind == "net")
            result.Price.Components.ShouldContain(c => c.Kind == "supplierNet");
        if (kind == "feeUnknown")
            Preview(catalogue, Input with { TotalBudget = "25000" })
                .Reasons.ShouldBe(["budgetExceeded"]);
    }

    [Theory]
    [InlineData("lower", "Z", "lowerComparablePrice")]
    [InlineData("complete", "Z", "completeFlightAndStay")]
    [InlineData("incomplete", "A", "equalByStableIds")]
    [InlineData("tie", "A", "equalByStableIds")]
    public void Equal_programmes_use_completeness_then_only_complete_prices_then_stable_ids(
        string variant,
        string id,
        string why
    )
    {
        var h = Catalogue.Hotels[0];
        var unknown = new OptionCharge("localFee", OptionChargeKind.UnknownMandatory, null);
        var a = h with
        {
            Id = "A",
            Charges = variant is "complete" or "incomplete"
                ? [Charge("hotelBase", "14000"), unknown]
                : [Charge("hotelBase", "14000")],
        };
        var z = h with
        {
            Id = "Z",
            Charges =
                variant == "incomplete"
                    ? [Charge("hotelBase", "12000"), unknown]
                    : [Charge("hotelBase", variant == "tie" ? "14000" : "12000")],
        };
        var result = Preview(new("A", [Catalogue.Flights[0]], [z, a])).Option.ShouldNotBeNull();
        result.Hotel.Id.ShouldBe(id);
        result.SelectionReason.ShouldBe(why);
    }

    [Fact]
    public void Equal_interest_coverage_prefers_more_stops_and_fully_equal_flights_use_ordinal_id()
    {
        var shortBrief = Input with { EndDate = Start.AddDays(1), Interests = [] };
        var flight = Catalogue.Flights[2];
        var late = flight with
        {
            Id = "A",
            Outbound = new("SVO", "LED", Local(28, 13, 45), Local(28, 15, 15)),
        };
        var early = flight with { Id = "Z" };
        var hotel = Catalogue.Hotels[2];
        var result = Preview(new("A", [late, early], [hotel]), shortBrief).Option.ShouldNotBeNull();
        result.Flight.Id.ShouldBe("Z");
        result.SelectionReason.ShouldBe("moreStops");
        var tied = Preview(new("A", [early, early with { Id = "A" }], [hotel]), shortBrief)
            .Option.ShouldNotBeNull();
        tied.Flight.Id.ShouldBe("A");
        tied.SelectionReason.ShouldBe("equalByStableIds");
    }

    [Fact]
    public void Window_cache_is_per_request_and_accepts_equivalent_hotels_and_empty_windows()
    {
        var h = Catalogue.Hotels[0];
        var brief = TripBrief.Create(Input).Value;
        var walks = new OptionWalks(brief, Facts, AsOf);
        var windows = OptionSchedule.Windows(OptionSchedule.Stay(Catalogue.Flights[0], h, brief)!);
        var same = OptionSchedule.Windows(
            OptionSchedule.Stay(Catalogue.Flights[0], h with { Id = "another" }, brief)!
        );
        var first = walks.Generate(windows, TestContext.Current.CancellationToken);
        walks.Generate(same, TestContext.Current.CancellationToken).ShouldBeSameAs(first);
        var fresh = new OptionWalks(brief, Facts, AsOf);
        fresh.Generate(same, TestContext.Current.CancellationToken).ShouldNotBeSameAs(first);
    }

    [Fact]
    public void A_single_night_can_have_no_walks_and_the_separate_flight_has_no_room()
    {
        var shortBrief = Input with { EndDate = Start.AddDays(1) };
        var shortOption = Preview(Catalogue, shortBrief).Option.ShouldNotBeNull();
        shortOption.Hotel.Id.ShouldBe("H3");
        shortOption.Hotel.Nights.ShouldBe(1);
        shortOption.Price.FlightAndStayComplete.ShouldBeFalse();
        var late = Catalogue.Hotels[2] with { EarliestCheckIn = new(17, 35) };
        Preview(new("A", [Catalogue.Flights[2]], [late]), shortBrief).Reasons.ShouldBe(["noWalks"]);
        Preview(Catalogue, Input with { StartDate = Start.AddDays(1) })
            .Reasons.ShouldBe(["hotelUnavailable"]);
    }

    private static DateTimeOffset Local(int day, int hour, int minute) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(3));
}
