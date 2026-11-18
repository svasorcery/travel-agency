using Shouldly;
using Travel.Modules.Trips.Core;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripFlightLinksTests
{
    [Fact]
    public void Changes_are_ordered_bounded_and_independent_of_calendar_and_other_trips()
    {
        var plan = TripPlanTests.Plan();
        var links = TripFlightLinks.Restore(plan.TripId, 0, []).Value;
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids.Take(4))
            links = links.Change(true, id, TripPlanTests.Now).Value;

        links.Revision.ShouldBe(4);
        links.Change(true, ids[0], TripPlanTests.Now).Value.ShouldBeSameAs(links);
        links.Change(false, ids[4], TripPlanTests.Now).Value.ShouldBeSameAs(links);
        links
            .Change(true, ids[4], TripPlanTests.Now)
            .FirstError.ShouldBe(TripErrors.FlightLinkLimitReached);
        var removed = links.Change(false, ids[1], TripPlanTests.Now).Value;
        var readded = removed.Change(true, ids[1], TripPlanTests.Now.AddHours(1)).Value;
        readded.Links.Select(x => x.OrderId).ShouldBe(new[] { ids[0], ids[2], ids[3], ids[1] });
        readded.Links[^1].LinkedAt.ShouldBe(TripPlanTests.Now.AddHours(1));
        readded.Revision.ShouldBe(6);
        TripFlightLinks
            .Restore(Guid.NewGuid(), 0, [])
            .Value.Change(true, ids[0], TripPlanTests.Now)
            .Value.Revision.ShouldBe(1);
        plan.Revision.ShouldBe(1);
        plan.Brief.StartDate.ShouldBe(TripPlanTests.Start);
        plan.Days.Count.ShouldBe(2);
        plan.KnownUserEstimateSubtotal.ShouldBe("0.00");
    }

    [Fact]
    public void Invalid_snapshots_fail_closed_and_revision_exhaustion_allows_only_checked_noops()
    {
        var trip = Guid.NewGuid();
        var link = new TripFlightLink(Guid.NewGuid(), TripPlanTests.Now);
        TripFlightLinks.Restore(trip, 0, [link]).IsError.ShouldBeTrue();
        TripFlightLinks.Restore(trip, 8, [link, link]).IsError.ShouldBeTrue();
        TripFlightLinks.Restore(trip, 1, [link with { LinkedAt = default }]).IsError.ShouldBeTrue();
        var fullRevision = TripFlightLinks.Restore(trip, long.MaxValue, [link]).Value;
        fullRevision
            .Change(true, link.OrderId, TripPlanTests.Now)
            .Value.ShouldBeSameAs(fullRevision);
        fullRevision
            .Change(false, link.OrderId, TripPlanTests.Now)
            .FirstError.ShouldBe(TripErrors.RevisionExhausted);
    }
}
