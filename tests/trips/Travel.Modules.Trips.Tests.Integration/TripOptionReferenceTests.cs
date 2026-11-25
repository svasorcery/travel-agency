using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripOptionReferenceTests
{
    [Theory]
    [InlineData("A", "07:30", "09:00", "14:00", 44)]
    [InlineData("B", "18:30", "20:00", "21:30", 0)]
    public async Task Reviewed_package_and_fictional_catalogue_reproduce_the_reference_calendar(
        string version,
        string departure,
        string arrival,
        string checkIn,
        int firstDayMinutes
    )
    {
        using var package = new CuratedSaintPetersburgFactsTests.Package();
        var facts = (await package.Source().ReadAsync(TestContext.Current.CancellationToken)).Value;
        var brief = TripBrief
            .Create(
                new(
                    "saint-petersburg",
                    "moscow",
                    new(2026, 10, 28),
                    new(2026, 10, 30),
                    2,
                    "RUB",
                    null,
                    ["architecture"],
                    "relaxed"
                )
            )
            .Value;
        var result = OptionPlanner.Preview(
            brief,
            facts,
            package.Clock.Now,
            FictionalOptionCatalogue.For(version, 2),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        var option = result.Value.Option.ShouldNotBeNull();
        option.Flight.Id.ShouldBe("RT1");
        option.Flight.Outbound.DepartsAt.ToString("HH:mm").ShouldBe(departure);
        option.Flight.Outbound.ArrivesAt.ToString("HH:mm").ShouldBe(arrival);
        option.Flight.Inbound.DepartsAt.ToString("HH:mm").ShouldBe("11:00");
        option.Flight.Inbound.ArrivesAt.ToString("HH:mm").ShouldBe("12:30");
        option.Hotel.Id.ShouldBe("H1");
        option.Hotel.Nights.ShouldBe(2);
        option.Hotel.CheckInAt.ToString("HH:mm").ShouldBe(checkIn);
        option.Hotel.CheckOutAt.ToString("HH:mm").ShouldBe("08:00");
        option.Price.KnownSubtotal.ShouldBe("30000.00");
        option.Price.UnpricedExpenses.ShouldBe(["meals", "transfers"]);
        option.CoveredInterests.ShouldBe(["architecture"]);
        option.SelectionReason.ShouldBe("lowerComparablePrice");
        var first = option.Days[0];
        first.ProgrammeMinutes.ShouldBe(firstDayMinutes);
        if (version == "A")
        {
            first.WindowStart.ShouldBe("14:20");
            first.WindowEnd.ShouldBe("17:40");
            first.Stops.Select(s => s.Id).ShouldBe(["spb-general-staff", "spb-pevchesky-bridge"]);
        }
        else
        {
            first.WindowStart.ShouldBeNull();
            first.WindowEnd.ShouldBeNull();
            first.Stops.ShouldBeEmpty();
            first.EmptyReason.ShouldBe("noWalkingWindow");
        }
        var middle = option.Days[1];
        middle.WindowStart.ShouldBe("10:20");
        middle.WindowEnd.ShouldBe("17:40");
        middle.Stops.Select(s => s.Id).ShouldBe(["spb-green-bridge", "spb-stroganov-palace"]);
        middle.ProgrammeMinutes.ShouldBe(38);
        option.Days[2].Stops.ShouldBeEmpty();
        option.Days[2].WindowStart.ShouldBeNull();
        option.Days[2].WindowEnd.ShouldBeNull();
        option.Days.SelectMany(d => d.Stops).ShouldAllBe(s => !s.AccessHoursKnown);
    }
}
