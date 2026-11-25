using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Core.Options;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripOptionValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Rejects_eighty_minutes_in_a_fifty_minute_window_using_the_source_edge_and_visits()
    {
        var setup = new Scenario(new(16, 30));
        var first = setup.Option.Days[0];
        first.WindowStart.ShouldBe("16:50");
        first.WindowEnd.ShouldBe("17:40");
        var invalid = first with
        {
            ProgrammeMinutes = 80,
            Stops =
            [
                setup.Stop("p0", "16:50", "17:20"),
                setup.Stop("p1", "17:42", "18:10", "e0", 22),
            ],
        };
        setup.Validate(invalid).ShouldBeFalse();
    }

    [Theory]
    [InlineData("17:20", "17:50", 50, false)]
    [InlineData("17:10", "17:40", 40, true)]
    public void Return_to_hotel_is_reserved_even_when_the_visit_alone_would_fit(
        string start,
        string end,
        int duration,
        bool valid
    )
    {
        // First possible stop 17:00. Hotel walking closes at 18:00 with a separate 20-minute return.
        var setup = new Scenario(new(16, 40));
        var day = setup.Option.Days[0] with
        {
            ProgrammeMinutes = duration,
            Stops = [setup.Stop("p0", start, end)],
        };
        setup.Validate(day).ShouldBe(valid);
    }

    [Fact]
    public void Flight_deadline_allows_a_stop_returning_exactly_at_check_out()
    {
        var setup = new Scenario(new(20, 0), laterReturn: true);
        var day = setup.Option.Days[1];
        var visit = day.Stops.ShouldHaveSingleItem();
        day.WindowStart.ShouldBe("10:20");
        day.WindowEnd.ShouldBe("10:50");
        setup.Option.Hotel.CheckOutAt.Hour.ShouldBe(11);
        setup.Option.Hotel.CheckOutAt.Minute.ShouldBe(10);
        (visit.DwellMinutes, visit.Start, visit.End).ShouldBe((30, "10:20", "10:50"));
        setup.Validate(setup.Option).ShouldBeTrue();
        var overrun = day with
        {
            ProgrammeMinutes = 40,
            Stops = [visit with { Start = "10:30", End = "11:00" }],
        };
        setup
            .Validate(setup.Option with { Days = [setup.Option.Days[0], overrun] })
            .ShouldBeFalse();
        var flight = setup.Option.Flight;
        var changedLocalTime = flight.Inbound with
        {
            DepartsAt = flight.Inbound.DepartsAt.ToUniversalTime(),
        };
        setup
            .Validate(setup.Option with { Flight = flight with { Inbound = changedLocalTime } })
            .ShouldBeFalse();
    }

    [Fact]
    public void Replay_rejects_wrong_directed_edge_wrong_order_and_duplicate_id()
    {
        var setup = new Scenario(new(14, 0));
        var valid = setup.Option.Days[0] with
        {
            ProgrammeMinutes = 80,
            Stops =
            [
                setup.Stop("p0", "14:20", "14:50"),
                setup.Stop("p1", "15:12", "15:40", "e0", 22),
            ],
        };
        setup.Validate(valid).ShouldBeTrue();
        setup
            .Validate(
                valid with
                {
                    Stops = [valid.Stops[0], valid.Stops[1] with { IncomingEdgeId = "e1" }],
                }
            )
            .ShouldBeFalse();
        setup
            .Validate(
                valid with
                {
                    Stops =
                    [
                        setup.Stop("p1", "14:20", "14:48"),
                        setup.Stop("p0", "15:10", "15:40", "e0", 22),
                    ],
                }
            )
            .ShouldBeFalse();
        setup
            .Validate(
                valid with
                {
                    Stops = [valid.Stops[0], setup.Stop("p0", "15:12", "15:42", "e0", 22)],
                }
            )
            .ShouldBeFalse();
    }

    [Fact]
    public void Known_access_requires_the_entire_visit_in_one_interval_and_wait_counts_in_elapsed_time()
    {
        var setup = new Scenario(new(14, 0), onlyKnownPoi: true);
        var day = setup.Option.Days[0];
        var stop = day.Stops.ShouldHaveSingleItem();
        (stop.Id, stop.Start, stop.End, stop.AccessHoursKnown).ShouldBe(
            ("p0", "16:00", "16:30", true)
        );
        day.ProgrammeMinutes.ShouldBe(130);
        setup.Validate(day).ShouldBeTrue();
        // First interval 15:00–15:20 cannot accommodate a 30-minute visit.
        setup
            .Validate(
                day with
                {
                    ProgrammeMinutes = 70,
                    Stops = [stop with { Start = "15:00", End = "15:30" }],
                }
            )
            .ShouldBeFalse();
        setup
            .Validate(day with { Stops = [stop with { AccessHoursKnown = false }] })
            .ShouldBeFalse();
    }

    private sealed class Scenario
    {
        private readonly TripBrief brief;
        private readonly ValidatedItineraryFacts facts;
        private readonly FlightOffer flight;
        private readonly StayOffer room;
        public TripOption Option { get; }

        public Scenario(TimeOnly checkIn, bool onlyKnownPoi = false, bool laterReturn = false)
        {
            var start = new DateOnly(2026, 10, 22);
            var end = start.AddDays(1);
            brief = TripBrief
                .Create(
                    new("saint-petersburg", "moscow", start, end, 2, "RUB", null, [], "relaxed")
                )
                .Value;
            var input = ItineraryTestFacts.Input("saint-petersburg", "spb-outdoor-profile-v1");
            var evidence = input.Evidence.ToList();
            var pois = input
                .Pois.Select(p =>
                {
                    var access = p.Access;
                    if (onlyKnownPoi)
                    {
                        var id = "access-" + p.Id;
                        evidence.Add(
                            ItineraryTestFacts
                                .EvidenceParameters(id, ItineraryFactKind.Access, p.Id)
                                .Build()
                                .Value
                        );
                        ItineraryWeeklyInterval[] intervals =
                            p.Id == "p0"
                                ?
                                [
                                    ItineraryWeeklyInterval.Create(start.DayOfWeek, 900, 920).Value,
                                    ItineraryWeeklyInterval
                                        .Create(start.DayOfWeek, 960, 1010)
                                        .Value,
                                ]
                                : [];
                        access = ItineraryAccess.Create(p.Id, true, id, intervals, []).Value;
                    }
                    return ItineraryPoi
                        .Create(
                            p.Id,
                            p.Title,
                            p.Tags,
                            p.Id == "p1" ? 28 : 30,
                            p.WikidataEntityId,
                            p.WikidataRevision,
                            p.IdentityEvidenceId,
                            p.Latitude,
                            p.Longitude,
                            access
                        )
                        .Value;
                })
                .ToArray();
            facts = (input with { Pois = pois, Evidence = evidence.ToArray() }).Build().Value;
            var catalogue = FictionalOptionCatalogue.For("A", 2);
            var first = catalogue.Flights[2];
            flight = first with
            {
                Outbound = Shift(first.Outbound),
                Inbound = Shift(first.Inbound!),
            };
            if (laterReturn)
            {
                var inbound = flight.Inbound!;
                flight = flight with
                {
                    Inbound = inbound with
                    {
                        DepartsAt = inbound.DepartsAt.AddMinutes(190),
                        ArrivesAt = inbound.ArrivesAt.AddMinutes(190),
                    },
                };
            }
            room = catalogue.Hotels[2] with { Nights = [start], EarliestCheckIn = checkIn };
            Option = OptionPlanner
                .Preview(brief, facts, ItineraryTestFacts.Now, new("test", [flight], [room]), Ct)
                .Value.Option.ShouldNotBeNull();
        }

        public OptionStop Stop(
            string id,
            string start,
            string end,
            string? edge = null,
            int walk = 0
        )
        {
            var poi = facts.Pois.Single(p => p.Id == id);
            return new(id, poi.Title, start, end, poi.DwellMinutes, edge, walk, poi.Access.IsKnown);
        }

        public bool Validate(OptionDay first) =>
            Validate(Option with { Days = [first, Option.Days[1]] });

        public bool Validate(TripOption option) =>
            OptionValidator.IsValid(brief, facts, ItineraryTestFacts.Now, flight, room, option, Ct);

        private static OptionLeg Shift(OptionLeg leg) =>
            leg with
            {
                DepartsAt = leg.DepartsAt.AddDays(-6),
                ArrivesAt = leg.ArrivesAt.AddDays(-6),
            };
    }
}
