using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Tests.Unit.Aggregates;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationScopeTests
{
    [Fact]
    public void Every_leg_and_party_binding_contributes_but_ticket_progress_does_not()
    {
        var quote = MultiLegReplayTests.FourLegQuote();
        var booking = CancellationDecisionTests.Held();
        booking.Apply(quote);
        var first = CancellationScope.Create(booking);
        first.IsError.ShouldBeFalse();
        booking.Apply(
            new OrderTicketed(
                new EquatableArray<string>(new[] { "fictional-ticket" }),
                CancellationTestData.Now
            )
        );
        CancellationScope.Create(booking).Value.ShouldBe(first.Value);
        booking.Apply(MultiLegReplayTests.FourLegReQuote(quote));
        CancellationScope.Create(booking).Value.ShouldNotBe(first.Value);
    }

    [Fact]
    public void Historical_reader_does_not_run_new_route_rules_during_scope_binding()
    {
        var booking = CancellationDecisionTests.Held();
        booking.Apply(
            JsonSerializer.Deserialize<OfferQuoted>(MultiLegReplayTests.HistoricalQuoteJson)!
        );
        booking.Itinerary!.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(18));
        CancellationScope.Create(booking).IsError.ShouldBeFalse();
    }
}
