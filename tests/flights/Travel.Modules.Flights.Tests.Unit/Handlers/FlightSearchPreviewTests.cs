using Shouldly;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Application.NlSearch;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Handlers;

public sealed class FlightSearchPreviewTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);
    private static readonly FlightSearchPreviewFragments Empty = new(
        null,
        null,
        null,
        null,
        null,
        null,
        []
    );

    [Fact]
    public void Complete_round_trip_keeps_grounded_values()
    {
        var draft = FlightSearchPreview.Normalize(
            "Из Москвы в Сочи 12 декабря 2026, обратно 15 декабря 2026, двое взрослых",
            Monday,
            new(
                "Из Москвы",
                "в Сочи",
                "12 декабря 2026",
                "обратно 15 декабря 2026",
                "двое взрослых",
                null,
                []
            )
        );

        draft.Origin.ShouldBe(new FlightSearchPreviewLocation("MOW", "Москва"));
        draft.Destination.ShouldBe(new FlightSearchPreviewLocation("AER", "Сочи"));
        draft.DepartureDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 12, 12), "explicit"));
        draft.ReturnDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 12, 15), "explicit"));
        draft.PassengerCount.ShouldBe(2);
        draft.TripType.ShouldBe("roundTrip");
        draft.Issues.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("MOW")]
    [InlineData("в Сочи")]
    public void Missing_origin_does_not_gain_an_unsupported_code_or_the_destination(string? origin)
    {
        var draft = FlightSearchPreview.Normalize(
            "Хочу в Сочи на выходные, нас двое",
            Monday,
            new(origin, "в Сочи", "на выходные", null, "нас двое", null, [])
        );

        draft.Origin.ShouldBeNull();
        draft.Destination.ShouldBe(new FlightSearchPreviewLocation("AER", "Сочи"));
        draft.PassengerCount.ShouldBe(2);
        draft.Issues.ShouldHaveSingleItem().Field.ShouldBe("origin");
        if (origin is null)
            draft.Issues[0].Reason.ShouldBe("missing");
    }

    [Theory]
    [InlineData("из Москвы или из Петербурга в Сочи")]
    [InlineData("из Москвы или Петербурга в Сочи")]
    [InlineData("из Москвы или Казани в Сочи")]
    public void Alternative_origins_require_a_choice_even_if_the_responder_picks_one(string query)
    {
        var draft = FlightSearchPreview.Normalize(
            query,
            Monday,
            Empty with
            {
                Origin = "из Москвы",
                Destination = "в Сочи",
            }
        );

        draft.Origin.ShouldBeNull();
        draft.Destination!.Code.ShouldBe("AER");
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("origin", "ambiguous"));
    }

    [Theory]
    [InlineData("не из Москвы, в Сочи", "из Москвы", null)]
    [InlineData("не из Москвы, а из Питера в Сочи", "из Питера", "LED")]
    [InlineData("из LED в AER", "LED", "LED")]
    [InlineData("из UFA в Сочи", "UFA", "UFA")]
    public void Origin_requires_positive_route_evidence(string query, string origin, string? code)
    {
        var draft = FlightSearchPreview.Normalize(query, Monday, Empty with { Origin = origin });

        draft.Origin?.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("нас двое, я с ребёнком")]
    [InlineData("нас двое, с детьми")]
    [InlineData("нас двое, бизнес-класс")]
    [InlineData("нас двое, цена в USD")]
    [InlineData("нас двое, составной маршрут")]
    [InlineData("нас двое, из Москвы в Сочи, затем в Питер")]
    [InlineData("нас двое, из Москвы в Сочи, обратно из Казани в Москву")]
    [InlineData("нас двое, из Москвы в Сочи на выходные, обратно в Казань")]
    [InlineData("нас двое, из Москвы в Сочи на выходные, обратно из Сочи в Казань")]
    [InlineData("нас двое, из Москвы в Сочи на выходные и вернуться в Казань")]
    [InlineData("нас двое, из Москвы в Сочи на выходные, бизнес")]
    public void Explicit_unsupported_conditions_are_reported_even_when_omitted(string query)
    {
        var draft = FlightSearchPreview.Normalize(query, Monday, Empty with { Party = "нас двое" });

        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("request", "unsupported"));
    }

    [Fact]
    public void Child_party_is_not_filled_as_two_adults()
    {
        var draft = FlightSearchPreview.Normalize(
            "нас двое, я с ребёнком",
            Monday,
            Empty with
            {
                Party = "нас двое",
            }
        );

        draft.PassengerCount.ShouldBeNull();
    }

    [Theory]
    [InlineData("нас двое, без детей", "нас двое", 2)]
    [InlineData("9 взрослых", "9 взрослых", 9)]
    [InlineData("1 взрослый", "1 взрослый", 1)]
    [InlineData("2 или 3 взрослых", "3 взрослых", null)]
    [InlineData("2–3 взрослых", "3 взрослых", null)]
    [InlineData("около 3 взрослых", "3 взрослых", null)]
    [InlineData("10 взрослых", "10 взрослых", null)]
    [InlineData("летим в Сочи", "нас двое", null)]
    public void Passenger_count_requires_a_single_explicit_supported_value(
        string query,
        string party,
        int? count
    )
    {
        var draft = FlightSearchPreview.Normalize(query, Monday, Empty with { Party = party });

        draft.PassengerCount.ShouldBe(count);
        if (count is not null)
            draft.Issues.ShouldNotContain(issue => issue.Field == "request");
    }

    [Theory]
    [InlineData(23, 11, 28, 11, 29)]
    [InlineData(28, 11, 28, 11, 29)]
    [InlineData(29, 12, 5, 12, 6)]
    public void Weekend_uses_the_next_local_saturday_including_today(
        int referenceDay,
        int departureMonth,
        int departureDay,
        int returnMonth,
        int returnDay
    )
    {
        var draft = FlightSearchPreview.Normalize(
            "на выходные",
            new(2026, 11, referenceDay),
            Empty with
            {
                Departure = "на выходные",
            }
        );

        draft.TripType.ShouldBe("roundTrip");
        draft.DepartureDate.ShouldBe(
            new FlightSearchPreviewDate(new(2026, departureMonth, departureDay), "suggested")
        );
        draft.ReturnDate.ShouldBe(
            new FlightSearchPreviewDate(new(2026, returnMonth, returnDay), "suggested")
        );
    }

    [Fact]
    public void Weekend_proposes_only_departure_for_explicit_one_way()
    {
        var draft = FlightSearchPreview.Normalize(
            "только туда на выходные",
            Monday,
            Empty with
            {
                Departure = "на выходные",
            }
        );

        draft.TripType.ShouldBe("oneWay");
        draft.DepartureDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 11, 28), "suggested"));
        draft.ReturnDate.ShouldBeNull();
        draft.Issues.ShouldNotContain(issue => issue.Field == "returnDate");
    }

    [Fact]
    public void Explicit_round_trip_requires_return_even_when_trip_type_fragment_is_omitted()
    {
        var draft = FlightSearchPreview.Normalize(
            "туда-обратно, вылет 12 декабря 2026",
            Monday,
            Empty with
            {
                Departure = "12 декабря 2026",
            }
        );

        draft.TripType.ShouldBe("roundTrip");
        draft.ReturnDate.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("returnDate", "missing"));
    }

    [Fact]
    public void Explicit_return_overrides_the_suggested_sunday()
    {
        var draft = FlightSearchPreview.Normalize(
            "на выходные, обратно 30 ноября 2026",
            Monday,
            Empty with
            {
                Departure = "на выходные",
                Return = "обратно 30 ноября 2026",
            }
        );

        draft.TripType.ShouldBe("roundTrip");
        draft.DepartureDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 11, 28), "suggested"));
        draft.ReturnDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 11, 30), "explicit"));
    }

    [Fact]
    public void A_missing_explicit_return_fragment_does_not_fall_back_to_sunday()
    {
        var draft = FlightSearchPreview.Normalize(
            "на выходные, обратно 30 ноября 2026",
            Monday,
            Empty with
            {
                Departure = "на выходные",
            }
        );

        draft.TripType.ShouldBe("roundTrip");
        draft.ReturnDate.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("returnDate", "missing"));
    }

    [Theory]
    [InlineData("на выходные, обратно в понедельник")]
    [InlineData("из Москвы в Сочи на выходные, обратно в Москву в понедельник")]
    [InlineData("из Москвы в Сочи на выходные и вернуться в Москву в понедельник")]
    public void An_omitted_return_weekday_does_not_fall_back_to_sunday(string query)
    {
        var draft = FlightSearchPreview.Normalize(
            query,
            Monday,
            Empty with
            {
                Departure = "на выходные",
            }
        );

        draft.TripType.ShouldBe("roundTrip");
        draft.ReturnDate.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("returnDate", "missing"));
        draft.Issues.ShouldNotContain(issue => issue.Field == "request");
    }

    [Fact]
    public void A_return_before_departure_needs_correction()
    {
        var draft = FlightSearchPreview.Normalize(
            "вылет 2026-12-15, обратно 2026-12-12",
            Monday,
            Empty with
            {
                Departure = "2026-12-15",
                Return = "2026-12-12",
            }
        );

        draft.DepartureDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 12, 15), "explicit"));
        draft.ReturnDate.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("returnDate", "invalid"));
    }

    [Theory]
    [InlineData("вылет 2026-12-12 или 2026-12-13", "2026-12-12")]
    [InlineData("вылет 12 или 13 декабря 2026", "13 декабря 2026")]
    public void Alternative_departures_are_ambiguous_regardless_of_the_fragment_selected(
        string text,
        string departure
    )
    {
        var draft = FlightSearchPreview.Normalize(
            text,
            Monday,
            Empty with
            {
                Departure = departure,
            }
        );

        draft.DepartureDate.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("departureDate", "ambiguous"));
    }

    [Fact]
    public void A_negated_return_cue_cannot_create_a_return_date()
    {
        var draft = FlightSearchPreview.Normalize(
            "вылет 12 декабря 2026, не обратно 15 декабря 2026",
            Monday,
            Empty with
            {
                Departure = "12 декабря 2026",
                Return = "15 декабря 2026",
            }
        );

        draft.TripType.ShouldBeNull();
        draft.DepartureDate.ShouldBe(new FlightSearchPreviewDate(new(2026, 12, 12), "explicit"));
        draft.ReturnDate.ShouldBeNull();
    }

    [Fact]
    public void An_explicit_one_way_and_explicit_return_require_a_trip_type_choice()
    {
        var draft = FlightSearchPreview.Normalize(
            "только туда, обратно 2026-12-15",
            Monday,
            Empty with
            {
                Return = "2026-12-15",
            }
        );

        draft.TripType.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("tripType", "ambiguous"));
    }

    [Fact]
    public void Weekend_outside_the_calendar_leaves_departure_unresolved()
    {
        var draft = FlightSearchPreview.Normalize(
            "на выходные",
            DateOnly.MaxValue,
            Empty with
            {
                Departure = "на выходные",
            }
        );

        draft.DepartureDate.ShouldBeNull();
        draft.ReturnDate.ShouldBeNull();
        draft.Issues.ShouldContain(new FlightSearchPreviewIssue("departureDate", "invalid"));
    }
}
